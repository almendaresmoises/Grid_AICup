import { Button } from "@workspace/ui/components/button"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  AlertTriangle,
  ArrowLeft,
  CheckCircle2,
  ClipboardCheck,
  Database,
  Pencil,
  Plus,
  Save,
  SlidersHorizontal,
  Sparkles,
  Trash2,
} from "lucide-react"
import { useMemo, useState, type ReactNode } from "react"
import { Link, useNavigate, useParams } from "react-router"
import { ErrorNote } from "@/components/error-note"
import { ApiError, api } from "@/lib/api"
import {
  parseColumns,
  type CanonicalDataset,
  type ParserConfiguration,
} from "@/lib/canonical"
import {
  autoMap,
  compliance,
  compilePipeline,
  datasetPartitions,
  emptyStudio,
  mappingComplete,
  newStep,
  parseExcelSample,
  parseSample,
  parserNamePattern,
  readStudio,
  stepCatalog,
  syncMappings,
  type SourceFormat,
  type StudioDocument,
  type StudioStep,
} from "@/lib/parser-studio"
import { inputClass, labelClass } from "@/lib/ui"

const steps = [
  { id: 1, label: "Setup", icon: Database },
  { id: 2, label: "Source", icon: Sparkles },
  { id: 3, label: "Transform", icon: SlidersHorizontal },
  { id: 4, label: "Review & publish", icon: ClipboardCheck },
]

export function ParserEditorPage() {
  const { parserId } = useParams()
  const existing = useQuery({
    queryKey: ["parsers", parserId],
    queryFn: () => api<ParserConfiguration>(`/api/app/parser/${parserId}`),
    enabled: Boolean(parserId),
  })
  const datasets = useQuery({
    queryKey: ["canonical-datasets"],
    queryFn: () => api<CanonicalDataset[]>("/api/app/canonical-dataset"),
  })

  if (parserId && existing.isLoading)
    return <p className="text-sm text-muted-foreground">Loading parser…</p>
  if (existing.error)
    return <ErrorNote message={(existing.error as Error).message} />

  return (
    <ParserStudio
      key={existing.data?.id ?? "new"}
      parser={existing.data}
      datasets={datasets.data ?? []}
    />
  )
}

function ParserStudio({
  parser,
  datasets,
}: {
  parser?: ParserConfiguration
  datasets: CanonicalDataset[]
}) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const loaded = parser ? readStudio(parser.pipelineJson) : null
  const [currentStep, setCurrentStep] = useState(1)
  const [name, setName] = useState(parser?.name ?? "")
  const [studio, setStudio] = useState<StudioDocument>(
    loaded?.studio ?? emptyStudio()
  )
  const [pipelineId] = useState(loaded?.pipelineId ?? crypto.randomUUID())
  const [revision, setRevision] = useState(loaded?.revision ?? 1)
  const [error, setError] = useState("")
  const [savedAt, setSavedAt] = useState("")
  const [activeStepId, setActiveStepId] = useState<string | null>(null)
  const [menuOpen, setMenuOpen] = useState(false)

  const dataset = datasets.find((item) => item.id === studio.datasetId)
  const sources = studio.columns
    .filter((column) => column.included)
    .map((column) => column.name)
  const report = compliance(studio.mappings)
  const nameInvalid = name.trim() !== "" && !parserNamePattern.test(name.trim())

  const save = useMutation({
    mutationFn: (publish: boolean) => {
      const nextRevision = parser ? revision + 1 : 1
      const pipeline = compilePipeline(
        name.trim(),
        studio,
        pipelineId,
        nextRevision
      )
      const body = JSON.stringify({
        name: name.trim(),
        fileFormat:
          studio.expectedFormat === "csv" ? "csv" : studio.expectedFormat,
        outputGcsPrefix: dataset?.gcsPrefix ?? "",
        isActive: publish,
        pipelineJson: JSON.stringify(pipeline),
      })
      const request = parser
        ? api<ParserConfiguration>(`/api/app/parser/${parser.id}`, {
            method: "PUT",
            body,
          })
        : api<ParserConfiguration>("/api/app/parser", { method: "POST", body })
      return request.then((saved) => ({ saved, nextRevision, publish }))
    },
    onSuccess: async ({ saved, nextRevision, publish }) => {
      setRevision(nextRevision)
      setSavedAt(new Date().toLocaleTimeString())
      await queryClient.invalidateQueries({ queryKey: ["parsers"] })
      if (publish) navigate("/configuration/manual/parsers")
      else if (!parser) navigate(`/configuration/manual/parsers/${saved.id}`)
    },
    onError: (caught) =>
      setError(
        caught instanceof ApiError
          ? caught.message
          : "Could not save the parser"
      ),
  })

  const remove = useMutation({
    mutationFn: () =>
      api(`/api/app/parser/${parser!.id}`, {
        method: "DELETE",
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["parsers"] })
      navigate("/configuration/manual/parsers")
    },
    onError: (caught) =>
      setError(
        caught instanceof ApiError
          ? caught.message
          : "Could not delete the parser"
      ),
  })

  const blockers = useMemo(() => {
    const items: string[] = []
    if (!name.trim()) items.push("Parser name is missing.")
    else if (!parserNamePattern.test(name.trim()))
      items.push(
        "Parser name may only contain letters, digits, and underscores."
      )
    if (!dataset) items.push("No canonical dataset is selected.")
    if (report.missing.length > 0)
      items.push(`Required mappings incomplete: ${report.missing.join(", ")}.`)
    if (
      studio.expectedFormat === "fixedWidth" &&
      studio.columns.some(
        (column) => column.included && (column.length ?? 0) < 1
      )
    ) {
      items.push("Every fixed-width column needs a length.")
    }
    return items
  }, [dataset, name, report.missing, studio.columns, studio.expectedFormat])

  function patch(partial: Partial<StudioDocument>) {
    setStudio((current) => ({ ...current, ...partial }))
  }

  function chooseDataset(datasetId: string) {
    const next = datasets.find((item) => item.id === datasetId)
    setStudio((current) => ({
      ...current,
      datasetId,
      mappings: syncMappings(next, current.mappings),
    }))
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex items-start gap-3">
          <Button
            variant="outline"
            size="icon-sm"
            aria-label="Back to parsers"
            render={<Link to="/configuration/manual/parsers" />}
          >
            <ArrowLeft />
          </Button>
          <div>
            <div className="flex items-center gap-2">
              <h2 className="text-lg font-medium">
                {parser ? `Edit parser: ${name || parser.name}` : "New parser"}
              </h2>
              {parser && !parser.isActive ? (
                <span className="bg-muted px-2 py-0.5 text-xs">Draft</span>
              ) : null}
            </div>
            <p className="text-xs text-muted-foreground">
              Configure the file, the transforms, and the canonical dataset this
              parser writes.
            </p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          {savedAt ? (
            <span className="flex items-center gap-1 text-xs text-muted-foreground">
              <CheckCircle2 className="size-3" /> Saved at {savedAt}
            </span>
          ) : null}
          {parser ? (
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={remove.isPending || save.isPending}
              onClick={() => {
                if (
                  window.confirm(
                    `Delete parser "${parser.name}"? This action cannot be undone.`
                  )
                ) {
                  remove.mutate()
                }
              }}
            >
              <Trash2 className="text-destructive" />
              Delete parser
            </Button>
          ) : null}
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={save.isPending || remove.isPending}
            onClick={() => submit(false)}
          >
            <Save />
            Save draft
          </Button>
          {currentStep !== 4 ? (
            <Button type="button" size="sm" onClick={() => setCurrentStep(4)}>
              <ClipboardCheck />
              Review & publish
            </Button>
          ) : null}
        </div>
      </div>

      <div className="flex border bg-card">
        {steps.map((step) => {
          const Icon = step.icon
          const active = currentStep === step.id
          const done = currentStep > step.id
          return (
            <button
              key={step.id}
              type="button"
              onClick={() => setCurrentStep(step.id)}
              className={`flex flex-1 items-center justify-center gap-2 border-b-2 px-2 py-3 text-xs ${active ? "border-primary text-primary" : done ? "border-primary/40" : "border-transparent text-muted-foreground"}`}
            >
              <span
                className={`flex size-5 items-center justify-center text-[10px] ${active || done ? "bg-primary text-primary-foreground" : "bg-muted"}`}
              >
                {done ? "✓" : step.id}
              </span>
              <Icon className="size-3.5" />
              {step.label}
            </button>
          )
        })}
      </div>

      {error ? <ErrorNote message={error} /> : null}

      {currentStep === 1 ? (
        <section className="flex flex-col gap-4 border bg-card p-4">
          <div>
            <h3 className="text-sm font-medium">Parser setup</h3>
            <p className="text-xs text-muted-foreground">
              Name the parser and choose the canonical dataset it produces.
              Files are uploaded when a run starts.
            </p>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <label className="flex flex-col gap-1.5">
              <span className={labelClass}>Parser name</span>
              <input
                className={inputClass}
                value={name}
                onChange={(event) => setName(event.target.value)}
                placeholder="SettlementFileParser"
              />
              {nameInvalid ? (
                <span className="text-xs text-destructive">
                  Use only letters, digits, and underscores.
                </span>
              ) : null}
            </label>
            <label className="flex flex-col gap-1.5">
              <span className={labelClass}>Description</span>
              <input
                className={inputClass}
                value={studio.description}
                onChange={(event) => patch({ description: event.target.value })}
                placeholder="What this parser reads and produces"
              />
            </label>
          </div>
          <div className="grid gap-4 border-t pt-4 sm:grid-cols-2">
            <label className="flex flex-col gap-1.5">
              <span className={labelClass}>Canonical dataset</span>
              <select
                className={inputClass}
                value={studio.datasetId}
                onChange={(event) => chooseDataset(event.target.value)}
              >
                <option value="">Select a dataset</option>
                {datasets.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.code} — {item.name}
                  </option>
                ))}
              </select>
            </label>
            <div className="flex flex-col gap-1.5">
              <span className={labelClass}>Partitions</span>
              <div className="flex min-h-9 flex-wrap items-center gap-1.5 border bg-muted/30 px-2 py-1.5">
                {datasetPartitions(dataset).length === 0 ? (
                  <span className="text-xs text-muted-foreground">
                    This dataset has no partitions.
                  </span>
                ) : (
                  datasetPartitions(dataset).map((column) => (
                    <span
                      key={column}
                      className="border bg-card px-2 py-0.5 font-mono text-[10px]"
                    >
                      {column}
                    </span>
                  ))
                )}
              </div>
            </div>
          </div>
          {dataset ? (
            <div className="flex flex-wrap gap-1">
              {parseColumns(dataset.schemaColumnsJson).map((column) => (
                <span
                  key={column.name}
                  className="border bg-muted px-1.5 py-0.5 font-mono text-[10px]"
                >
                  {column.name}{" "}
                  <span className="text-muted-foreground">
                    ({column.polarsType})
                  </span>
                </span>
              ))}
            </div>
          ) : null}
          <p className="border-t pt-3 font-mono text-xs text-muted-foreground">
            Output {dataset?.outputUri || (dataset ? dataset.gcsPrefix : "—")}
          </p>
        </section>
      ) : null}

      {currentStep === 2 ? <SourceStep studio={studio} patch={patch} /> : null}

      {currentStep === 3 ? (
        <TransformStep
          studio={studio}
          dataset={dataset}
          sources={sources}
          report={report}
          activeStepId={activeStepId}
          menuOpen={menuOpen}
          setMenuOpen={setMenuOpen}
          setActiveStepId={setActiveStepId}
          patch={patch}
        />
      ) : null}

      {currentStep === 4 ? (
        <section className="grid gap-4 lg:grid-cols-2">
          <Summary title="Identity" onEdit={() => setCurrentStep(1)}>
            <Row label="Name" value={name || "—"} />
            <Row label="Description" value={studio.description || "—"} />
            <Row
              label="Dataset"
              value={dataset ? `${dataset.code} — ${dataset.name}` : "—"}
            />
            <Row
              label="Output"
              value={dataset?.outputUri || dataset?.gcsPrefix || "—"}
            />
          </Summary>
          <Summary title="Source" onEdit={() => setCurrentStep(2)}>
            <Row label="Format" value={studio.expectedFormat} />
            <Row label="Sample" value={studio.sampleFileName || "—"} />
            <Row
              label="Delimiter"
              value={studio.expectedFormat === "csv" ? studio.delimiter : "—"}
            />
            <Row label="Header" value={studio.headerMode} />
            <Row label="Columns" value={String(sources.length)} />
          </Summary>
          <Summary title="Transforms" onEdit={() => setCurrentStep(3)}>
            {studio.steps.filter((item) => item.enabled).length === 0 ? (
              <p className="text-xs text-muted-foreground">
                No transformation steps.
              </p>
            ) : (
              studio.steps
                .filter((item) => item.enabled)
                .map((item) => (
                  <p key={item.id} className="text-xs">
                    {item.label}
                    {item.source ? ` · ${item.source}` : ""}
                  </p>
                ))
            )}
            <p className="mt-2 text-xs text-muted-foreground">
              {report.satisfied}/{report.totalRequired} required columns mapped
            </p>
          </Summary>
          <div className="flex flex-col gap-3 border bg-card p-4">
            <h3 className="text-sm font-medium">Readiness</h3>
            {blockers.length === 0 ? (
              <p className="flex items-center gap-2 text-sm">
                <CheckCircle2 className="size-4" /> Ready to publish.
              </p>
            ) : (
              blockers.map((item) => (
                <p
                  key={item}
                  className="flex items-start gap-2 text-xs text-destructive"
                >
                  <AlertTriangle className="mt-0.5 size-3.5 shrink-0" /> {item}
                </p>
              ))
            )}
            <Button
              type="button"
              disabled={save.isPending || blockers.length > 0}
              onClick={() => submit(true)}
            >
              {save.isPending ? "Saving…" : "Publish parser"}
            </Button>
          </div>
        </section>
      ) : null}
    </div>
  )

  function submit(publish: boolean) {
    setError("")
    if (!name.trim() || nameInvalid) {
      setError("Parser name may only contain letters, digits, and underscores.")
      setCurrentStep(1)
      return
    }
    if (publish && blockers.length > 0) return
    save.mutate(publish)
  }
}

function SourceStep({
  studio,
  patch,
}: {
  studio: StudioDocument
  patch: (partial: Partial<StudioDocument>) => void
}) {
  const [sourceError, setSourceError] = useState("")
  return (
    <section className="flex flex-col gap-4 border bg-card p-4">
      <div>
        <h3 className="text-sm font-medium">Source file</h3>
        <p className="text-xs text-muted-foreground">
          Describe the file that will be uploaded for each run, then inspect a
          sample.
        </p>
      </div>
      <div className="grid gap-4 sm:grid-cols-3">
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Expected format</span>
          <select
            className={inputClass}
            value={studio.expectedFormat}
            onChange={(event) =>
              patch({ expectedFormat: event.target.value as SourceFormat })
            }
          >
            <option value="csv">Delimited</option>
            <option value="excel">Excel</option>
            <option value="fixedWidth">Fixed width</option>
          </select>
        </label>
        {studio.expectedFormat === "csv" ? (
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Delimiter</span>
            <input
              className={inputClass}
              value={studio.delimiter}
              maxLength={1}
              onChange={(event) => patch({ delimiter: event.target.value })}
            />
          </label>
        ) : null}
        {studio.expectedFormat === "excel" ? (
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Sheet name</span>
            <input
              className={inputClass}
              value={studio.sheetName}
              onChange={(event) => patch({ sheetName: event.target.value })}
              placeholder="First sheet if blank"
            />
          </label>
        ) : null}
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Header</span>
          <select
            className={inputClass}
            value={studio.headerMode}
            onChange={(event) =>
              patch({
                headerMode: event.target.value as StudioDocument["headerMode"],
              })
            }
          >
            <option value="firstRow">First row</option>
            <option value="rowIndex">Specific row</option>
            <option value="none">No header</option>
          </select>
        </label>
      </div>
      {studio.headerMode === "rowIndex" ? (
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Header row index (starting at 0)</span>
          <input
            className={inputClass}
            type="number"
            min={0}
            value={studio.headerRowIndex}
            onChange={(event) =>
              patch({ headerRowIndex: Math.max(0, Number(event.target.value)) })
            }
          />
        </label>
      ) : null}
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={studio.removeTotalRows}
          onChange={(event) => patch({ removeTotalRows: event.target.checked })}
        />
        Drop total and trailer rows
      </label>
      {studio.removeTotalRows ? (
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Total-row keywords</span>
          <input
            className={inputClass}
            value={studio.totalRowKeywords}
            onChange={(event) =>
              patch({ totalRowKeywords: event.target.value })
            }
          />
        </label>
      ) : null}
      {studio.expectedFormat !== "excel" ? (
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Sample file</span>
          <input
            className="text-sm"
            type="file"
            accept=".csv,.txt"
            onChange={(event) => {
              const file = event.target.files?.[0]
              if (!file) return
              setSourceError("")
              file
                .text()
                .then((text) => {
                  if (studio.expectedFormat === "fixedWidth") {
                    const rows = text
                      .split(/\r?\n/)
                      .filter(Boolean)
                      .slice(0, 12)
                    patch({
                      sampleFileName: file.name,
                      sampleRows: rows.map((row) =>
                        studio.columns.map((column) =>
                          row.slice(
                            column.start ?? 0,
                            (column.start ?? 0) + (column.length ?? 1)
                          )
                        )
                      ),
                    })
                    return
                  }
                  const parsed = parseSample(
                    text,
                    studio.delimiter,
                    studio.headerMode,
                    studio.headerRowIndex
                  )
                  patch({
                    sampleFileName: file.name,
                    columns: parsed.columns,
                    sampleRows: parsed.sampleRows,
                  })
                })
                .catch((caught: unknown) =>
                  setSourceError(
                    caught instanceof Error
                      ? caught.message
                      : "Could not read the source file."
                  )
                )
            }}
          />
        </label>
      ) : (
        <div className="flex flex-col gap-2">
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Sample Excel workbook (optional)</span>
            <input
              className="text-sm"
              type="file"
              accept=".xlsx,.xls"
              onChange={(event) => {
                const file = event.target.files?.[0]
                if (!file) return
                setSourceError("")
                file
                  .arrayBuffer()
                  .then((buffer) => {
                    const parsed = parseExcelSample(
                      buffer,
                      studio.sheetName,
                      studio.headerMode,
                      studio.headerRowIndex
                    )
                    patch({
                      sampleFileName: file.name,
                      sheetName: parsed.sheetName,
                      columns: parsed.columns,
                      sampleRows: parsed.sampleRows,
                    })
                  })
                  .catch((caught: unknown) =>
                    setSourceError(
                      caught instanceof Error
                        ? caught.message
                        : "Could not read Excel file."
                    )
                  )
              }}
            />
          </label>
          <div className="flex items-center justify-between">
            <span className={labelClass}>Excel source column names</span>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() =>
                patch({
                  columns: [...studio.columns, { name: "", included: true }],
                })
              }
            >
              <Plus /> Add column
            </Button>
          </div>
          <p className="text-xs text-muted-foreground">
            Auto-populated from sample workbook or enter the exact header names
            manually.
          </p>
          {studio.columns.map((column, index) => (
            <div key={index} className="flex gap-2">
              <input
                className={inputClass}
                aria-label={`Source column ${index + 1}`}
                value={column.name}
                onChange={(event) =>
                  updateColumn(index, { name: event.target.value })
                }
              />
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() =>
                  patch({
                    columns: studio.columns.filter((_, item) => item !== index),
                  })
                }
              >
                Remove
              </Button>
            </div>
          ))}
        </div>
      )}
      {sourceError ? <ErrorNote message={sourceError} /> : null}
      {studio.expectedFormat === "fixedWidth" ? (
        <div className="flex flex-col gap-2">
          <div className="flex items-center justify-between">
            <span className={labelClass}>Column positions</span>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() =>
                patch({
                  columns: [
                    ...studio.columns,
                    { name: "", included: true, start: 0, length: 1 },
                  ],
                })
              }
            >
              <Plus /> Add column
            </Button>
          </div>
          {studio.columns.map((column, index) => (
            <div
              key={index}
              className="grid gap-2 sm:grid-cols-[1fr_100px_100px_auto]"
            >
              <input
                className={inputClass}
                aria-label={`Column ${index + 1} name`}
                value={column.name}
                onChange={(event) =>
                  updateColumn(index, { name: event.target.value })
                }
              />
              <input
                className={inputClass}
                aria-label={`Column ${index + 1} start`}
                type="number"
                value={column.start ?? 0}
                onChange={(event) =>
                  updateColumn(index, { start: Number(event.target.value) })
                }
              />
              <input
                className={inputClass}
                aria-label={`Column ${index + 1} length`}
                type="number"
                value={column.length ?? 1}
                onChange={(event) =>
                  updateColumn(index, { length: Number(event.target.value) })
                }
              />
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() =>
                  patch({
                    columns: studio.columns.filter((_, item) => item !== index),
                  })
                }
              >
                Remove
              </Button>
            </div>
          ))}
        </div>
      ) : null}
      {studio.columns.length > 0 && studio.expectedFormat !== "fixedWidth" ? (
        <div className="flex flex-wrap gap-1">
          {studio.columns.map((column) => (
            <span
              key={column.name}
              className="border px-1.5 py-0.5 font-mono text-[10px]"
            >
              {column.name}
            </span>
          ))}
        </div>
      ) : null}
      {studio.sampleRows.length > 0 ? (
        <div className="overflow-x-auto border">
          <table className="w-full text-xs">
            <thead>
              <tr className="bg-muted/40 text-left">
                {studio.columns.map((column) => (
                  <th key={column.name} className="px-2 py-1.5 font-medium">
                    {column.name}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {studio.sampleRows.map((row, index) => (
                <tr key={index} className="border-t">
                  {row.map((cell, cellIndex) => (
                    <td key={cellIndex} className="px-2 py-1.5 font-mono">
                      {cell}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  )

  function updateColumn(
    index: number,
    partial: Partial<StudioDocument["columns"][number]>
  ) {
    patch({
      columns: studio.columns.map((column, item) =>
        item === index ? { ...column, ...partial } : column
      ),
    })
  }
}

function TransformStep({
  studio,
  dataset,
  sources,
  report,
  activeStepId,
  menuOpen,
  setMenuOpen,
  setActiveStepId,
  patch,
}: {
  studio: StudioDocument
  dataset: CanonicalDataset | undefined
  sources: string[]
  report: ReturnType<typeof compliance>
  activeStepId: string | null
  menuOpen: boolean
  setMenuOpen: (open: boolean) => void
  setActiveStepId: (id: string | null) => void
  patch: (partial: Partial<StudioDocument>) => void
}) {
  const active = studio.steps.find((item) => item.id === activeStepId) ?? null
  return (
    <section className="flex flex-col gap-4 border bg-card p-4">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b pb-2">
        <div>
          <h3 className="text-sm font-medium">
            Transformation studio{" "}
            {dataset ? (
              <span className="font-normal text-muted-foreground">
                · {dataset.name}
              </span>
            ) : null}
          </h3>
          <p className="text-xs text-muted-foreground">
            Map source columns onto the canonical schema, then add the steps
            that shape them.
          </p>
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => patch({ mappings: autoMap(studio.mappings, sources) })}
        >
          <Sparkles /> Match exact names
        </Button>
      </div>
      <div
        className={`border p-3 text-xs ${report.complete ? "border-primary/30" : ""}`}
      >
        {report.complete ? (
          <p className="flex items-center gap-2">
            <CheckCircle2 className="size-4" /> {report.satisfied}/
            {report.totalRequired} required columns mapped
          </p>
        ) : (
          <p className="flex items-center gap-2">
            <AlertTriangle className="size-4" /> {report.missing.length}{" "}
            required columns still need a source
          </p>
        )}
        <div className="mt-2 flex flex-wrap gap-1">
          {studio.mappings.map((mapping) => (
            <span
              key={mapping.target}
              className={`border px-1.5 py-0.5 font-mono text-[10px] ${mappingComplete(mapping) ? "border-primary/40" : mapping.required ? "border-destructive/40" : ""}`}
            >
              {mappingComplete(mapping) ? "✓" : mapping.required ? "✗" : "○"}{" "}
              {mapping.target}
            </span>
          ))}
        </div>
      </div>
      <div className="flex flex-col gap-2">
        {studio.mappings.map((mapping) => (
          <div
            key={mapping.target}
            className="grid items-center gap-2 sm:grid-cols-[160px_140px_1fr]"
          >
            <span className="font-mono text-xs">{mapping.target}</span>
            <select
              className={inputClass}
              aria-label={`${mapping.target} source kind`}
              value={mapping.sourceKind}
              onChange={(event) =>
                updateMapping(mapping.target, {
                  sourceKind: event.target.value as ColumnKind,
                })
              }
            >
              <option value="unmapped">Unmapped</option>
              <option value="column">Column</option>
              <option value="constant">Constant</option>
            </select>
            {mapping.sourceKind === "column" ? (
              <select
                className={inputClass}
                aria-label={`${mapping.target} source column`}
                value={mapping.sourceColumn}
                onChange={(event) =>
                  updateMapping(mapping.target, {
                    sourceColumn: event.target.value,
                  })
                }
              >
                <option value="">Select a column</option>
                {sources.map((source) => (
                  <option key={source} value={source}>
                    {source}
                  </option>
                ))}
              </select>
            ) : mapping.sourceKind === "constant" ? (
              <input
                className={inputClass}
                aria-label={`${mapping.target} constant`}
                value={mapping.constant}
                onChange={(event) =>
                  updateMapping(mapping.target, {
                    constant: event.target.value,
                  })
                }
              />
            ) : (
              <span className="text-xs text-muted-foreground">
                {mapping.required ? "Required" : "Optional"}
              </span>
            )}
          </div>
        ))}
        {studio.mappings.length === 0 ? (
          <p className="text-xs text-muted-foreground">
            Choose a canonical dataset in Setup before mapping columns.
          </p>
        ) : null}
      </div>
      <div className="flex items-center justify-between border-t pt-3">
        <h3 className="text-sm font-medium">Steps</h3>
        <div className="relative">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => setMenuOpen(!menuOpen)}
          >
            <Plus /> Add step
          </Button>
          {menuOpen ? (
            <div className="absolute right-0 z-10 mt-1 w-64 border bg-card py-1 text-xs shadow-md">
              {stepCatalog.map((item, index) => {
                const previous = stepCatalog[index - 1]
                return (
                  <div key={item.kind}>
                    {item.group !== previous?.group ? (
                      <p className="px-3 py-1 text-[10px] tracking-wide text-muted-foreground uppercase">
                        {item.group}
                      </p>
                    ) : null}
                    <button
                      type="button"
                      className="flex w-full px-3 py-1.5 text-left hover:bg-muted"
                      onClick={() => {
                        const created = newStep(item.kind, sources[0] ?? "")
                        patch({ steps: [...studio.steps, created] })
                        setActiveStepId(created.id)
                        setMenuOpen(false)
                      }}
                    >
                      {item.label}
                    </button>
                  </div>
                )
              })}
            </div>
          ) : null}
        </div>
      </div>
      {studio.steps.map((item) => (
        <div key={item.id} className="border">
          <div className="flex items-center justify-between gap-2 px-3 py-2">
            <button
              type="button"
              className="text-left text-sm"
              onClick={() =>
                setActiveStepId(activeStepId === item.id ? null : item.id)
              }
            >
              {item.label}
              <span className="ml-2 text-xs text-muted-foreground">
                {item.source || "No column"}
              </span>
            </button>
            <div className="flex items-center gap-1">
              <label className="flex items-center gap-1 text-xs text-muted-foreground">
                <input
                  type="checkbox"
                  checked={item.enabled}
                  onChange={(event) =>
                    updateStep(item.id, { enabled: event.target.checked })
                  }
                />
                On
              </label>
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                aria-label={`Remove ${item.label}`}
                onClick={() =>
                  patch({
                    steps: studio.steps.filter((step) => step.id !== item.id),
                  })
                }
              >
                <Trash2 />
              </Button>
            </div>
          </div>
          {active?.id === item.id ? (
            <StepFields
              item={item}
              sources={sources}
              onChange={(next) => updateStep(item.id, next)}
            />
          ) : null}
        </div>
      ))}
    </section>
  )

  function updateMapping(
    target: string,
    partial: Partial<StudioDocument["mappings"][number]>
  ) {
    patch({
      mappings: studio.mappings.map((mapping) =>
        mapping.target === target ? { ...mapping, ...partial } : mapping
      ),
    })
  }

  function updateStep(id: string, partial: Partial<StudioStep>) {
    patch({
      steps: studio.steps.map((step) =>
        step.id === id ? { ...step, ...partial } : step
      ),
    })
  }
}

type ColumnKind = "unmapped" | "column" | "constant"

function StepFields({
  item,
  sources,
  onChange,
}: {
  item: StudioStep
  sources: string[]
  onChange: (partial: Partial<StudioStep>) => void
}) {
  const setParam = (key: string, value: string) =>
    onChange({ params: { ...item.params, [key]: value } })
  return (
    <div className="grid gap-3 border-t px-3 py-3 sm:grid-cols-2">
      {item.kind !== "filterPrefix" ? (
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Column</span>
          <select
            className={inputClass}
            value={item.source}
            onChange={(event) => onChange({ source: event.target.value })}
          >
            <option value="">Select a column</option>
            {sources.map((source) => (
              <option key={source} value={source}>
                {source}
              </option>
            ))}
          </select>
        </label>
      ) : (
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Column to test</span>
          <select
            className={inputClass}
            value={item.source}
            onChange={(event) => onChange({ source: event.target.value })}
          >
            {sources.map((source) => (
              <option key={source} value={source}>
                {source}
              </option>
            ))}
          </select>
        </label>
      )}
      {item.kind === "parseDate" ? (
        <Field
          label="Format"
          value={item.params.format ?? ""}
          onChange={(value) => setParam("format", value)}
        />
      ) : null}
      {item.kind === "pad" ? (
        <>
          <Field
            label="Length"
            value={item.params.length ?? ""}
            onChange={(value) => setParam("length", value)}
          />
          <Field
            label="Pad character"
            value={item.params.padChar ?? ""}
            onChange={(value) => setParam("padChar", value)}
          />
          <SelectField
            label="Side"
            value={item.params.side ?? "left"}
            options={["left", "right"]}
            onChange={(value) => setParam("side", value)}
          />
        </>
      ) : null}
      {item.kind === "trimChars" ? (
        <>
          <Field
            label="Characters"
            value={item.params.characters ?? ""}
            onChange={(value) => setParam("characters", value)}
          />
          <SelectField
            label="Side"
            value={item.params.side ?? "start"}
            options={["start", "end", "both"]}
            onChange={(value) => setParam("side", value)}
          />
        </>
      ) : null}
      {item.kind === "extract" ? (
        <>
          <Field
            label="Start"
            value={item.params.offset ?? "0"}
            onChange={(value) => setParam("offset", value)}
          />
          <Field
            label="Length"
            value={item.params.length ?? "1"}
            onChange={(value) => setParam("length", value)}
          />
        </>
      ) : null}
      {item.kind === "replace" ? (
        <>
          <Field
            label="Find"
            value={item.params.pattern ?? ""}
            onChange={(value) => setParam("pattern", value)}
          />
          <Field
            label="Replace with"
            value={item.params.replacement ?? ""}
            onChange={(value) => setParam("replacement", value)}
          />
        </>
      ) : null}
      {item.kind === "case" ? (
        <label className="flex flex-col gap-1.5 sm:col-span-2">
          <span className={labelClass}>Rules</span>
          <textarea
            className="min-h-20 border px-3 py-2 font-mono text-xs"
            value={item.params.rules ?? ""}
            placeholder={"S => SALE\nR => REFUND"}
            onChange={(event) => setParam("rules", event.target.value)}
          />
        </label>
      ) : null}
      {item.kind === "filterPrefix" ? (
        <Field
          label="Prefixes"
          value={item.params.keywords ?? ""}
          onChange={(value) => setParam("keywords", value)}
        />
      ) : null}
    </div>
  )
}

function Field({
  label,
  value,
  onChange,
}: {
  label: string
  value: string
  onChange: (value: string) => void
}) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className={labelClass}>{label}</span>
      <input
        className={inputClass}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
    </label>
  )
}

function SelectField({
  label,
  value,
  options,
  onChange,
}: {
  label: string
  value: string
  options: string[]
  onChange: (value: string) => void
}) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className={labelClass}>{label}</span>
      <select
        className={inputClass}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      >
        {options.map((option) => (
          <option key={option} value={option}>
            {option}
          </option>
        ))}
      </select>
    </label>
  )
}

function Summary({
  title,
  onEdit,
  children,
}: {
  title: string
  onEdit: () => void
  children: ReactNode
}) {
  return (
    <div className="border bg-card p-4">
      <div className="mb-2 flex items-center justify-between border-b pb-2">
        <h3 className="text-sm font-medium">{title}</h3>
        <button
          type="button"
          className="inline-flex items-center gap-1 text-xs text-primary"
          onClick={onEdit}
        >
          <Pencil className="size-3" /> Edit
        </button>
      </div>
      {children}
    </div>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="grid grid-cols-3 gap-2 py-1 text-xs">
      <span className="text-muted-foreground">{label}</span>
      <span className="col-span-2 break-all">{value}</span>
    </div>
  )
}
