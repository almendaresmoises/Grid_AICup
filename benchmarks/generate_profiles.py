import json
import uuid
from pathlib import Path

def create_bundle(tier: str, rule_definitions: list) -> dict:
    gl_code = f"BM_{tier.upper()}_GL"
    bank_code = f"BM_{tier.upper()}_BANK"
    
    profile_name = f"Benchmark {tier.capitalize()} Reconciliation"
    description = f"Automated {tier.capitalize()} Reconciliation Benchmark Profile (100,000 rows test)"
    
    definition = {
        "name": profile_name,
        "version": 1,
        "datasets": [gl_code, bank_code],
        "rules": rule_definitions,
    }
    
    datasets = [
        {
            "code": gl_code,
            "name": f"{tier.capitalize()} SAP General Ledger",
            "description": f"Benchmark General Ledger dataset for {tier}",
            "gcsPrefix": f"canonical/bm_{tier}_gl",
            "schemaColumnsJson": json.dumps([
                {"name": "company_code", "polarsType": "pl.String"},
                {"name": "gl_account", "polarsType": "pl.String"},
                {"name": "document_no", "polarsType": "pl.String"},
                {"name": "posting_date", "polarsType": "pl.Date"},
                {"name": "document_date", "polarsType": "pl.Date"},
                {"name": "reference", "polarsType": "pl.String"},
                {"name": "amount", "polarsType": "pl.Decimal(18,2)"},
                {"name": "currency", "polarsType": "pl.String"},
                {"name": "text", "polarsType": "pl.String"},
                {"name": "assignment", "polarsType": "pl.String"},
            ]),
            "partitionColumnsJson": "[]",
        },
        {
            "code": bank_code,
            "name": f"{tier.capitalize()} Bank Statement",
            "description": f"Benchmark Bank Statement dataset for {tier}",
            "gcsPrefix": f"canonical/bm_{tier}_bank",
            "schemaColumnsJson": json.dumps([
                {"name": "company_code", "polarsType": "pl.String"},
                {"name": "statement_no", "polarsType": "pl.String"},
                {"name": "value_date", "polarsType": "pl.Date"},
                {"name": "booking_date", "polarsType": "pl.Date"},
                {"name": "bank_reference", "polarsType": "pl.String"},
                {"name": "transaction_type", "polarsType": "pl.String"},
                {"name": "amount", "polarsType": "pl.Decimal(18,2)"},
                {"name": "currency", "polarsType": "pl.String"},
                {"name": "description", "polarsType": "pl.String"},
            ]),
            "partitionColumnsJson": "[]",
        },
    ]
    
    def make_pipeline(parser_name: str, ds_code: str, columns: list, mappings: list) -> str:
        pipeline_uuid = str(uuid.uuid5(uuid.NAMESPACE_DNS, f"{tier}_{ds_code}"))
        pipeline_ident = parser_name.replace(" ", "_")
        return json.dumps({
            "dslVersion": "2.0",
            "pipeline": {
                "id": pipeline_uuid,
                "revision": 1,
                "name": pipeline_ident,
            },
            "inputs": [
                {
                    "id": "main",
                    "connector": "csv",
                    "binding": "execution.input",
                    "options": {"hasHeader": True, "delimiter": ","},
                }
            ],
            "steps": [
                {
                    "id": "__studio",
                    "op": "profile",
                    "enabled": False,
                    "args": {
                        "studio": {
                            "description": f"Parser for {parser_name}",
                            "expectedFormat": "csv",
                            "columns": [{"name": c, "included": True} for c in columns],
                            "mappings": mappings,
                            "steps": [],
                        }
                    },
                }
            ],
            "quality": {
                "defaultErrorAction": "quarantine",
                "quarantine": {"enabled": True, "relativePath": "_quarantine"},
            },
            "sink": {
                "connector": "parquet",
                "binding": "execution.output",
                "writeMode": "overwrite",
            },
        })

    gl_cols = ["company_code", "gl_account", "document_no", "posting_date", "document_date", "reference", "amount", "currency", "text", "assignment"]
    gl_mappings = [
        {"sourceColumn": c, "target": c, "targetType": "date" if "date" in c else "decimal" if c == "amount" else "string", "sourceKind": "column"}
        for c in gl_cols
    ]
    
    bank_cols = ["company_code", "statement_no", "value_date", "booking_date", "bank_reference", "transaction_type", "amount", "currency", "description"]
    bank_mappings = [
        {"sourceColumn": c, "target": c, "targetType": "date" if "date" in c else "decimal" if c == "amount" else "string", "sourceKind": "column"}
        for c in bank_cols
    ]
    
    parsers = [
        {
            "name": f"BM {tier.capitalize()} GL Parser",
            "fileFormat": "csv",
            "pipelineJson": make_pipeline(f"BM_{tier.upper()}_GL_Parser", gl_code, gl_cols, gl_mappings),
            "outputGcsPrefix": f"canonical/bm_{tier}_gl",
            "isActive": True,
        },
        {
            "name": f"BM {tier.capitalize()} Bank Parser",
            "fileFormat": "csv",
            "pipelineJson": make_pipeline(f"BM_{tier.upper()}_BANK_Parser", bank_code, bank_cols, bank_mappings),
            "outputGcsPrefix": f"canonical/bm_{tier}_bank",
            "isActive": True,
        },
    ]
    
    return {
        "formatVersion": 1,
        "exportedAt": "2026-10-18T12:00:00Z",
        "package": {
            "name": profile_name,
            "description": description,
        },
        "profile": {
            "name": profile_name,
            "description": description,
            "version": 1,
            "status": "Approved",
            "createdWithAi": False,
            "definitionJson": json.dumps(definition, indent=2),
        },
        "datasets": datasets,
        "parsers": parsers,
    }


def generate_all():
    out_dir = Path("benchmarks/profiles")
    out_dir.mkdir(parents=True, exist_ok=True)
    
    # 1. Simple Rules (2 rules)
    simple_gl = "BM_SIMPLE_GL"
    simple_bank = "BM_SIMPLE_BANK"
    simple_rules = [
        {
            "id": "EXACT_REF_AMT_DATE",
            "type": "exact",
            "datasets": [simple_gl, simple_bank],
            "keys": [
                {"type": "text", "match_mode": "exact", "fields": {simple_gl: "reference", simple_bank: "bank_reference"}},
                {"type": "amount", "match_mode": "exact", "fields": {simple_gl: "amount", simple_bank: "amount"}},
                {"type": "date", "match_mode": "exact", "fields": {simple_gl: "posting_date", simple_bank: "value_date"}},
            ],
            "remarks": "Exact match on Reference, Amount, Date",
        },
        {
            "id": "EXACT_REF_AMT",
            "type": "exact",
            "datasets": [simple_gl, simple_bank],
            "keys": [
                {"type": "text", "match_mode": "exact", "fields": {simple_gl: "reference", simple_bank: "bank_reference"}},
                {"type": "amount", "match_mode": "exact", "fields": {simple_gl: "amount", simple_bank: "amount"}},
            ],
            "remarks": "Exact match on Reference and Amount",
        },
    ]
    simple_bundle = create_bundle("simple", simple_rules)
    (out_dir / "simple_bundle.json").write_text(json.dumps(simple_bundle, indent=2), encoding="utf-8")
    
    # 2. Intermediate Rules (5 rules)
    inter_gl = "BM_INTERMEDIATE_GL"
    inter_bank = "BM_INTERMEDIATE_BANK"
    intermediate_rules = [
        {
            "id": "INT_EXACT_REF_AMT_DATE",
            "type": "exact",
            "datasets": [inter_gl, inter_bank],
            "keys": [
                {"type": "text", "match_mode": "exact", "fields": {inter_gl: "reference", inter_bank: "bank_reference"}},
                {"type": "amount", "match_mode": "exact", "fields": {inter_gl: "amount", inter_bank: "amount"}},
                {"type": "date", "match_mode": "exact", "fields": {inter_gl: "posting_date", inter_bank: "value_date"}},
            ],
            "remarks": "Pass 1: Exact Reference + Amount + Date",
        },
        {
            "id": "INT_EXACT_REF_AMT",
            "type": "exact",
            "datasets": [inter_gl, inter_bank],
            "keys": [
                {"type": "text", "match_mode": "exact", "fields": {inter_gl: "reference", inter_bank: "bank_reference"}},
                {"type": "amount", "match_mode": "exact", "fields": {inter_gl: "amount", inter_bank: "amount"}},
            ],
            "remarks": "Pass 2: Exact Reference + Amount",
        },
        {
            "id": "INT_DATE_TOLERANCE",
            "type": "partial",
            "datasets": [inter_gl, inter_bank],
            "keys": [
                {"type": "text", "match_mode": "exact", "fields": {inter_gl: "reference", inter_bank: "bank_reference"}},
                {"type": "amount", "match_mode": "exact", "fields": {inter_gl: "amount", inter_bank: "amount"}},
                {"type": "date", "match_mode": "fuzzy", "algorithm": "date_proximity", "tolerance": "3", "min_score": 0.8, "fields": {inter_gl: "posting_date", inter_bank: "value_date"}},
            ],
            "remarks": "Pass 3: Date tolerance (+/- 3 days)",
        },
        {
            "id": "INT_FEE_TOLERANCE",
            "type": "partial",
            "datasets": [inter_gl, inter_bank],
            "keys": [
                {"type": "text", "match_mode": "exact", "fields": {inter_gl: "reference", inter_bank: "bank_reference"}},
                {"type": "date", "match_mode": "exact", "fields": {inter_gl: "posting_date", inter_bank: "value_date"}},
                {"type": "amount", "match_mode": "fuzzy", "algorithm": "numeric_best_practice", "tolerance": "2.00", "min_score": 0.8, "fields": {inter_gl: "amount", inter_bank: "amount"}},
            ],
            "remarks": "Pass 4: Minor fee variance (+/- 2.00)",
        },
        {
            "id": "INT_DOC_AMT",
            "type": "exact",
            "datasets": [inter_gl, inter_bank],
            "keys": [
                {"type": "text", "match_mode": "exact", "fields": {inter_gl: "document_no", inter_bank: "statement_no"}},
                {"type": "amount", "match_mode": "exact", "fields": {inter_gl: "amount", inter_bank: "amount"}},
            ],
            "remarks": "Pass 5: Secondary Document/Statement Number match",
        },
    ]
    inter_bundle = create_bundle("intermediate", intermediate_rules)
    (out_dir / "intermediate_bundle.json").write_text(json.dumps(inter_bundle, indent=2), encoding="utf-8")

    # 3. Enterprise Rules (9 rules)
    ent_gl = "BM_ENTERPRISE_GL"
    ent_bank = "BM_ENTERPRISE_BANK"
    enterprise_rules = [
        {
            "id": "ENT_EXACT_CORP_REF_AMT_DATE",
            "type": "exact",
            "datasets": [ent_gl, ent_bank],
            "keys": [
                {"type": "text", "match_mode": "exact", "fields": {ent_gl: "company_code", ent_bank: "company_code"}},
                {"type": "text", "match_mode": "exact", "fields": {ent_gl: "currency", ent_bank: "currency"}},
                {"type": "text", "match_mode": "exact", "fields": {ent_gl: "reference", ent_bank: "bank_reference"}},
                {"type": "amount", "match_mode": "exact", "fields": {ent_gl: "amount", ent_bank: "amount"}},
                {"type": "date", "match_mode": "exact", "fields": {ent_gl: "posting_date", ent_bank: "value_date"}},
            ],
            "remarks": "Pass 1: Multi-entity full exact match",
        },
        {
            "id": "ENT_EXACT_REF_AMT_DATE",
            "type": "exact",
            "datasets": [ent_gl, ent_bank],
            "keys": [
                {"type": "text", "match_mode": "exact", "fields": {ent_gl: "reference", ent_bank: "bank_reference"}},
                {"type": "amount", "match_mode": "exact", "fields": {ent_gl: "amount", ent_bank: "amount"}},
                {"type": "date", "match_mode": "exact", "fields": {ent_gl: "posting_date", ent_bank: "value_date"}},
            ],
            "remarks": "Pass 2: Standard Exact Reference + Amount + Date",
        },
        {
            "id": "ENT_EXACT_REF_AMT",
            "type": "exact",
            "datasets": [ent_gl, ent_bank],
            "keys": [
                {"type": "text", "match_mode": "exact", "fields": {ent_gl: "reference", ent_bank: "bank_reference"}},
                {"type": "amount", "match_mode": "exact", "fields": {ent_gl: "amount", ent_bank: "amount"}},
            ],
            "remarks": "Pass 3: Exact Reference + Amount",
        },
        {
            "id": "ENT_DATE_TOLERANCE_5D",
            "type": "partial",
            "datasets": [ent_gl, ent_bank],
            "keys": [
                {"type": "text", "match_mode": "exact", "fields": {ent_gl: "company_code", ent_bank: "company_code"}},
                {"type": "text", "match_mode": "exact", "fields": {ent_gl: "reference", ent_bank: "bank_reference"}},
                {"type": "amount", "match_mode": "exact", "fields": {ent_gl: "amount", ent_bank: "amount"}},
                {"type": "date", "match_mode": "fuzzy", "algorithm": "date_proximity", "tolerance": "5", "min_score": 0.8, "fields": {ent_gl: "posting_date", ent_bank: "value_date"}},
            ],
            "remarks": "Pass 4: Entity scoped date tolerance (+/- 5 days)",
        },
        {
            "id": "ENT_FEE_TOLERANCE_5",
            "type": "partial",
            "datasets": [ent_gl, ent_bank],
            "keys": [
                {"type": "text", "match_mode": "exact", "fields": {ent_gl: "company_code", ent_bank: "company_code"}},
                {"type": "text", "match_mode": "exact", "fields": {ent_gl: "reference", ent_bank: "bank_reference"}},
                {"type": "date", "match_mode": "exact", "fields": {ent_gl: "posting_date", ent_bank: "value_date"}},
                {"type": "amount", "match_mode": "fuzzy", "algorithm": "numeric_best_practice", "tolerance": "5.00", "min_score": 0.8, "fields": {ent_gl: "amount", ent_bank: "amount"}},
            ],
            "remarks": "Pass 5: Scoped fee tolerance (+/- 5.00)",
        },
        {
            "id": "ENT_DOC_STMT_EXACT",
            "type": "exact",
            "datasets": [ent_gl, ent_bank],
            "keys": [
                {"type": "text", "match_mode": "exact", "fields": {ent_gl: "document_no", ent_bank: "statement_no"}},
                {"type": "amount", "match_mode": "exact", "fields": {ent_gl: "amount", ent_bank: "amount"}},
            ],
            "remarks": "Pass 6: Document to statement number exact match",
        },
        {
            "id": "ENT_FUZZY_REF_AMOUNT",
            "type": "partial",
            "datasets": [ent_gl, ent_bank],
            "keys": [
                {"type": "amount", "match_mode": "exact", "fields": {ent_gl: "amount", ent_bank: "amount"}},
                {"type": "date", "match_mode": "exact", "fields": {ent_gl: "posting_date", ent_bank: "value_date"}},
                {"type": "text", "match_mode": "fuzzy", "algorithm": "damerau_levenshtein", "min_score": 0.80, "fields": {ent_gl: "reference", ent_bank: "bank_reference"}},
            ],
            "remarks": "Pass 7: Anchored Amount and Date with Fuzzy Reference (Levenshtein >= 80%)",
        },
        {
            "id": "ENT_DIVERGENT_KEY_TOLERANCE",
            "type": "partial",
            "datasets": [ent_gl, ent_bank],
            "keys": [
                {"type": "text", "match_mode": "exact", "fields": {ent_gl: "reference", ent_bank: "bank_reference"}},
                {"type": "date", "match_mode": "exact", "fields": {ent_gl: "posting_date", ent_bank: "value_date"}},
                {"type": "amount", "match_mode": "divergent", "expected_mismatch": True, "fields": {ent_gl: "amount", ent_bank: "amount"}},
            ],
            "remarks": "Pass 8: Divergent amount tolerance for FX variance items",
        },
        {
            "id": "ENT_FALLBACK_ASSIGNMENT",
            "type": "exact",
            "datasets": [ent_gl, ent_bank],
            "keys": [
                {"type": "text", "match_mode": "exact", "fields": {ent_gl: "assignment", ent_bank: "statement_no"}},
                {"type": "amount", "match_mode": "exact", "fields": {ent_gl: "amount", ent_bank: "amount"}},
            ],
            "remarks": "Pass 9: Fallback Assignment to Statement matching",
        },
    ]
    ent_bundle = create_bundle("enterprise", enterprise_rules)
    (out_dir / "enterprise_bundle.json").write_text(json.dumps(ent_bundle, indent=2), encoding="utf-8")
    print("Generated Simple, Intermediate, and Enterprise profile bundles in benchmarks/profiles/")

if __name__ == "__main__":
    generate_all()
