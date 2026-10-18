import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path
from uuid import uuid4
from unittest.mock import patch

import polars as pl

from workers.etl_worker.core.base_job import ExecutionContext
from workers.etl_worker.core.storage_provider import StorageProvider
from workers.etl_worker.core.delta_utils import DeltaUtils
from workers.etl_worker.jobs.parsers.v2_pipeline_runner import execute_v2
from workers.etl_worker.message_consumer import process_payload


def pipeline():
    return {
        "dslVersion": "2.0",
        "pipeline": {"id": str(uuid4()), "revision": 1, "name": "receipts"},
        "inputs": [{"id": "main", "connector": "csv", "binding": "execution.input", "options": {"hasHeader": True}}],
        "steps": [{"id": "amount", "op": "cast", "args": {"source": "amount", "target": "amount", "dataType": "decimal(18,2)"}}],
        "sink": {"connector": "parquet", "binding": "execution.output", "writeMode": "overwrite"},
    }


CONTRACT = [{"name": "receipt_id", "type": "string", "required": True}, {"name": "amount", "type": "decimal", "required": True}]


class CanonicalPipelineTests(unittest.TestCase):
    def test_invalid_card_mask_is_detected_before_source_is_overwritten(self):
        from workers.etl_worker.jobs.parsers.engine import ParserPipeline, PolarsPipelineCompiler
        from workers.etl_worker.jobs.parsers.engine.compiler import ERROR_CODES_COLUMN
        definition = pipeline()
        definition["steps"] = [{"id": "mask-company", "op": "mask", "args": {
            "source": "company_code", "target": "company_code", "keepStart": 6, "keepEnd": 4,
        }}]
        result = PolarsPipelineCompiler(ParserPipeline.model_validate(definition)).compile({
            "main": pl.DataFrame({"company_code": ["101"]}).lazy(),
        })
        self.assertEqual(result.valid_frame().collect().height, 0)
        quarantine = result.quarantine_frame().collect()
        self.assertEqual(quarantine.height, 1)
        self.assertEqual(quarantine[ERROR_CODES_COLUMN].to_list(), [["mask-company_FAILED"]])

    def test_invalid_cast_is_quarantined_even_when_target_overwrites_source(self):
        from workers.etl_worker.jobs.parsers.engine import ParserPipeline, PolarsPipelineCompiler
        result = PolarsPipelineCompiler(ParserPipeline.model_validate(pipeline())).compile({
            "main": pl.DataFrame({"receipt_id": ["001"], "amount": ["invalid"]}).lazy(),
        })
        self.assertEqual(result.valid_frame().collect().height, 0)
        self.assertEqual(result.quarantine_frame().collect().height, 1)

    def test_cast_does_not_invent_cents_scaling_or_card_masking(self):
        from workers.etl_worker.jobs.parsers.engine import ParserPipeline, PolarsPipelineCompiler
        definition = pipeline()
        definition["steps"].append({"id": "id", "op": "cast", "args": {
            "source": "receipt_id", "target": "receipt_id", "dataType": "string",
        }})
        result = PolarsPipelineCompiler(ParserPipeline.model_validate(definition)).compile({
            "main": pl.DataFrame({"receipt_id": ["5448301234560002"], "amount": ["000500"]}).lazy(),
        }).valid_frame().collect()
        self.assertEqual(result["receipt_id"].to_list(), ["5448301234560002"])
        self.assertEqual(str(result["amount"][0]), "500.00")

    def test_optional_null_dates_are_preserved_and_counted(self):
        context = self.execute("receipt_id,amount,trx_date\n001,10.00,2026-08-10\n002,20.00,\n", fields=[
            *CONTRACT, {"name": "trx_date", "type": "date", "required": False},
        ])
        self.assertEqual(context._parser_report["input_rows"], 2)
        self.assertEqual(context._parser_report["output_rows"], 2)
        self.assertEqual(context._parser_report["dropped_rows"], 0)
        profile = next(p for p in context._parser_report["column_profiles"] if p["column"] == "trx_date")
        self.assertEqual(profile["null_count"], 1)

    def execute(self, text, mode="full", fields=None):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        source = Path(directory.name) / "source.csv"
        source.write_bytes(text.encode("utf-8"))
        context = ExecutionContext(task_id="test", input_location=str(source), output_location=str(Path(directory.name) / "data.parquet"), pipeline_definition=pipeline(), execution_mode=mode, parameters={"output_contract": fields if fields is not None else CONTRACT})
        execute_v2(context, StorageProvider())
        return context

    def test_outputs_same_exact_canonical_values_in_parquet_and_csv(self):
        context = self.execute('receipt_id,amount\n001,90071992547409.93\n002,20.10\n')
        parquet = pl.read_parquet(context.output_location).select("receipt_id", "amount")
        csv = pl.read_csv(context.output_location + ".csv", infer_schema=False)
        self.assertEqual(csv["receipt_id"].to_list(), ["001", "002"])
        self.assertEqual(csv["amount"].to_list(), ["90071992547409.93", "20.10"])
        self.assertEqual(parquet["receipt_id"].to_list(), csv["receipt_id"].to_list())
        self.assertEqual([str(value) for value in parquet["amount"]], csv["amount"].to_list())

    def test_missing_required_value_fails_before_output_is_published(self):
        with self.assertRaisesRegex(ValueError, "Required dataset column 'amount'"):
            self.execute("receipt_id,amount\n001,\n")

    def test_optional_null_value_is_preserved(self):
        context = self.execute("receipt_id,amount\n001,\n", "preview", [CONTRACT[0], {**CONTRACT[1], "required": False}])
        self.assertEqual(context._parser_report["preview_rows"], [{"receipt_id": "001", "amount": None}])

    def test_multiline_csv_cell_is_one_record(self):
        context = self.execute('receipt_id,amount,note\n001,10.00,"one\ntwo"\n', "preview", [*CONTRACT, {"name": "note", "type": "string", "required": False}])
        self.assertEqual(context._parser_report["output_rows"], 1)
        self.assertEqual(context._parser_report["preview_rows"][0]["note"], "one\ntwo")

    def test_preview_writes_no_output(self):
        context = self.execute("receipt_id,amount\n001,10.00\n", "preview")
        self.assertFalse(Path(context.output_location).exists())
        self.assertFalse(Path(context.output_location + ".csv").exists())

    def test_append_keeps_existing_rows(self):
        with tempfile.TemporaryDirectory() as directory:
            target = str(Path(directory) / "data.parquet")
            DeltaUtils.write_append(pl.DataFrame({"id": ["001"]}), target, [])
            DeltaUtils.write_append(pl.DataFrame({"id": ["002"]}), target, [])
            self.assertEqual(pl.read_parquet(target)["id"].to_list(), ["001", "002"])

    def test_repeated_snapshot_write_preserves_source_duplicates(self):
        with tempfile.TemporaryDirectory() as directory:
            target = str(Path(directory) / "data.parquet")
            frame = pl.DataFrame({"id": ["001", "001"]})
            DeltaUtils.write_idempotent(frame, target)
            DeltaUtils.write_idempotent(frame, target)
            self.assertEqual(pl.read_parquet(target)["id"].to_list(), ["001", "001"])

    @unittest.skipUnless(shutil.which("node"), "Node.js is required to verify the browser compiler")
    def test_browser_compiler_preserves_swapped_and_reused_source_columns(self):
        script = r'''
const fs = require('fs'), ts = require('typescript'), vm = require('vm');
function load(path, dependency) {
    const exports = {};
    const source = ts.transpileModule(fs.readFileSync(path, 'utf8'), {compilerOptions: {module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022}}).outputText;
    vm.runInNewContext(source, {exports, require: dependency, crypto: require('crypto').webcrypto});
    return exports;
}
const canonical = load('apps/web/src/lib/canonical.ts', require);
const studio = load('apps/web/src/lib/parser-studio.ts', name => name === '@/lib/canonical' ? canonical : require(name));
const document = studio.emptyStudio();
document.columns = ['left', 'right', 'amount'].map(name => ({name, included: true}));
document.mappings = [['left', 'right', 'pl.String'], ['right', 'left', 'pl.String'], ['receipt_copy', 'left', 'pl.String'], ['amount', 'amount', 'pl.Decimal(18,2)']].map(([target, sourceColumn, targetType]) => ({target, sourceColumn, targetType, required: true, sourceKind: 'column', constant: ''}));
console.log(JSON.stringify({pipeline: studio.compilePipeline('receipts', document, require('crypto').randomUUID(), 1), sample: studio.parseSample('id,note\n001,"one\ntwo ""quoted"""', ',', 'firstRow', 0)}));
'''
        root = Path(__file__).resolve().parents[2]
        compiled = json.loads(subprocess.run(["node", "-e", script], cwd=root, check=True, capture_output=True, text=True).stdout)
        self.assertEqual(compiled["sample"]["sampleRows"], [["001", 'one\ntwo "quoted"']])
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "input.csv"
            source.write_bytes(b"left,right,amount\n001,R-1,90071992547409.93\n")
            context = ExecutionContext(input_location=str(source), output_location=str(Path(directory) / "output.parquet"), pipeline_definition=compiled["pipeline"], execution_mode="preview")
            execute_v2(context, StorageProvider())
            record = context._parser_report["preview_rows"][0]
            self.assertEqual(record["left"], "R-1")
            self.assertEqual(record["right"], "001")
            self.assertEqual(record["receipt_copy"], "001")
            self.assertEqual(record["amount"], "90071992547409.93")

    @patch("workers.etl_worker.message_consumer.get_worker_runner_secret", return_value="secret")
    @patch("workers.etl_worker.message_consumer._report")
    @patch("workers.etl_worker.message_consumer.execute_v2", side_effect=ValueError("Bad input"))
    @patch("workers.etl_worker.message_consumer.StorageProvider")
    def test_invalid_input_reports_failed_without_poison_message_retry(self, storage, execute, report, secret):
        storage.return_value.read_file.return_value = b"input"
        process_payload({"run_id": "test", "pipeline": pipeline(), "input_uri": "gs://bucket/in.csv", "output_uri": "gs://bucket/out.parquet"})
        report.assert_called_once_with("test", "Failed", "Bad input", {})

    @patch("workers.etl_worker.message_consumer.get_worker_runner_secret", return_value="secret")
    @patch("workers.etl_worker.message_consumer._report", side_effect=ConnectionError("API unavailable"))
    @patch("workers.etl_worker.message_consumer.execute_v2")
    @patch("workers.etl_worker.message_consumer.StorageProvider")
    def test_reporting_failure_propagates_for_pubsub_redelivery(self, storage, execute, report, secret):
        storage.return_value.read_file.return_value = b"input"
        def complete(context, provider):
            context._rows_processed = 1
        execute.side_effect = complete
        with self.assertRaises(ConnectionError):
            process_payload({"run_id": "test", "pipeline": pipeline(), "input_uri": "gs://bucket/in.csv", "output_uri": "gs://bucket/out.parquet"})


    def test_excel_pipeline_execution_with_headers_and_offset(self):
        with tempfile.TemporaryDirectory() as directory:
            excel_path = Path(directory) / "input.xlsx"
            pl.DataFrame([
                {"c1": "Title", "c2": "Info"},
                {"c1": "receipt_id", "c2": "amount"},
                {"c1": "001", "c2": "55.20"},
            ]).write_excel(excel_path, include_header=False)
            pipeline_def = {
                "dslVersion": "2.0",
                "pipeline": {"id": str(uuid4()), "revision": 1, "name": "excel_test"},
                "inputs": [{
                    "id": "main",
                    "connector": "excel",
                    "binding": "execution.input",
                    "options": {"hasHeader": True, "skipRows": 1},
                }],
                "steps": [
                    {"id": "c1", "op": "cast", "args": {"source": "amount", "target": "amount", "dataType": "decimal(18,2)"}},
                    {"id": "c2", "op": "cast", "args": {"source": "receipt_id", "target": "receipt_id", "dataType": "string"}},
                ],
                "sink": {"connector": "parquet", "binding": "execution.output", "writeMode": "overwrite"},
            }
            context = ExecutionContext(
                input_location=str(excel_path),
                output_location=str(Path(directory) / "output.parquet"),
                pipeline_definition=pipeline_def,
                execution_mode="full",
                parameters={"output_contract": CONTRACT},
            )
            execute_v2(context, StorageProvider())
            parquet = pl.read_parquet(context.output_location)
            self.assertEqual(parquet["receipt_id"].to_list(), ["001"])
            self.assertEqual([str(v) for v in parquet["amount"]], ["55.20"])

    def test_excel_pipeline_execution_headerless_with_column_names(self):
        with tempfile.TemporaryDirectory() as directory:
            excel_path = Path(directory) / "input_no_header.xlsx"
            pl.DataFrame([
                {"c1": "hdr", "c2": "val"},
                {"c1": "002", "c2": "77.10"},
            ]).write_excel(excel_path, include_header=False)
            pipeline_def = {
                "dslVersion": "2.0",
                "pipeline": {"id": str(uuid4()), "revision": 1, "name": "excel_test_no_header"},
                "inputs": [{
                    "id": "main",
                    "connector": "excel",
                    "binding": "execution.input",
                    "options": {"hasHeader": False, "skipRows": 1, "columnNames": ["receipt_id", "amount"]},
                }],
                "steps": [
                    {"id": "c1", "op": "cast", "args": {"source": "amount", "target": "amount", "dataType": "decimal(18,2)"}},
                    {"id": "c2", "op": "cast", "args": {"source": "receipt_id", "target": "receipt_id", "dataType": "string"}},
                ],
                "sink": {"connector": "parquet", "binding": "execution.output", "writeMode": "overwrite"},
            }
            context = ExecutionContext(
                input_location=str(excel_path),
                output_location=str(Path(directory) / "output.parquet"),
                pipeline_definition=pipeline_def,
                execution_mode="full",
                parameters={"output_contract": CONTRACT},
            )
            execute_v2(context, StorageProvider())
            parquet = pl.read_parquet(context.output_location)
            self.assertEqual(parquet["receipt_id"].to_list(), ["002"])
            self.assertEqual([str(v) for v in parquet["amount"]], ["77.10"])

    def test_unwraps_supported_csv_from_zip_archive(self):
        import zipfile
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        zip_path = Path(directory.name) / "bundle.zip"
        with zipfile.ZipFile(zip_path, "w") as zf:
            zf.writestr("transactions.csv", "receipt_id,amount\n009,42.50\n")

        context = ExecutionContext(
            task_id="zip-test",
            input_location=str(zip_path),
            output_location=str(Path(directory.name) / "data.parquet"),
            pipeline_definition=pipeline(),
            execution_mode="full",
            parameters={"output_contract": CONTRACT},
        )
        execute_v2(context, StorageProvider())
        parquet = pl.read_parquet(context.output_location)
        self.assertEqual(parquet["receipt_id"].to_list(), ["009"])
        self.assertEqual([str(v) for v in parquet["amount"]], ["42.50"])

    def test_unwraps_supported_xlsx_from_zip_archive(self):
        import zipfile
        with tempfile.TemporaryDirectory() as directory:
            excel_path = Path(directory) / "data.xlsx"
            pl.DataFrame([
                {"receipt_id": "010", "amount": 88.40},
            ]).write_excel(excel_path)

            zip_path = Path(directory) / "excel_bundle.zip"
            with zipfile.ZipFile(zip_path, "w") as zf:
                zf.write(excel_path, arcname="nested/data.xlsx")

            pipeline_def = {
                "dslVersion": "2.0",
                "pipeline": {"id": str(uuid4()), "revision": 1, "name": "excel_zip_test"},
                "inputs": [{
                    "id": "main",
                    "connector": "excel",
                    "binding": "execution.input",
                    "options": {"hasHeader": True},
                }],
                "steps": [
                    {"id": "c1", "op": "cast", "args": {"source": "amount", "target": "amount", "dataType": "decimal(18,2)"}},
                    {"id": "c2", "op": "cast", "args": {"source": "receipt_id", "target": "receipt_id", "dataType": "string"}},
                ],
                "sink": {"connector": "parquet", "binding": "execution.output", "writeMode": "overwrite"},
            }
            context = ExecutionContext(
                task_id="zip-excel-test",
                input_location=str(zip_path),
                output_location=str(Path(directory) / "output.parquet"),
                pipeline_definition=pipeline_def,
                execution_mode="full",
                parameters={"output_contract": CONTRACT},
            )
            execute_v2(context, StorageProvider())
            parquet = pl.read_parquet(context.output_location)
            self.assertEqual(parquet["receipt_id"].to_list(), ["010"])
            self.assertEqual([str(v) for v in parquet["amount"]], ["88.40"])

    def test_lineage_sidecar_and_content_hashes_are_generated(self):
        context = self.execute("receipt_id,amount\n001,10.00\n002,20.00\n")
        self.assertIn("output_hash", context._parser_report)
        self.assertIsNotNone(context._parser_report["output_hash"])
        self.assertIn("lineage_hash", context._parser_report)
        self.assertIsNotNone(context._parser_report["lineage_hash"])
        self.assertIn("lineage_location", context._parser_report)
        lineage_file = context._parser_report["lineage_location"]
        self.assertTrue(Path(lineage_file).exists())
        lineage_df = pl.read_csv(lineage_file)
        self.assertIn("original_row_number", lineage_df.columns)
        self.assertIn("raw_content_id", lineage_df.columns)
        self.assertEqual(lineage_df["original_row_number"].to_list(), [1, 2])


if __name__ == "__main__":
    unittest.main()
