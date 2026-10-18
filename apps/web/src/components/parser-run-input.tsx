import { Button } from "@workspace/ui/components/button"
import { useQuery } from "@tanstack/react-query"
import { useEffect, useId, useRef, useState } from "react"
import { Link } from "react-router"
import {
  AlertCircle,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  Clock,
  FileSpreadsheet,
  FileText,
  Loader2,
  UploadCloud,
  X,
} from "lucide-react"
import { cn } from "cn"
import { api } from "@/lib/api"
import type { CanonicalDataset, ParserConfiguration, ParserRun } from "@/lib/canonical"
import { readStudio } from "@/lib/parser-studio"
import { inputClass } from "@/lib/ui"

function formatBytes(bytes: number) {
  if (bytes === 0) return "0 B"
  const k = 1024
  const sizes = ["B", "KB", "MB", "GB"]
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`
}

function getFileIcon(name: string) {
  const lower = name.toLowerCase()
  if (lower.endsWith(".xlsx") || lower.endsWith(".xls") || lower.endsWith(".csv")) {
    return <FileSpreadsheet className="size-5" />
  }
  return <FileText className="size-5" />
}

export interface ParserRunInputProps {
  datasetId: string
  dataset?: CanonicalDataset
  code: string
  parsers: ParserConfiguration[]
  selectedParserId: string
  onSelectParserId: (parserId: string) => void
  stagedFile: File | null
  onStageFile: (file: File | null) => void
  selectedRunId: string
  onSelectRunId: (runId: string) => void
  status?: "idle" | "uploading" | "parsing" | "succeeded" | "failed"
  error?: string | null
  disabled?: boolean
}

export function ParserRunInput({
  datasetId,
  dataset,
  code,
  parsers,
  selectedParserId,
  onSelectParserId,
  stagedFile,
  onStageFile,
  selectedRunId,
  onSelectRunId,
  status = "idle",
  error,
  disabled = false,
}: ParserRunInputProps) {
  const [isDragging, setIsDragging] = useState(false)
  const [showHistory, setShowHistory] = useState(false)
  const fileInputRef = useRef<HTMLInputElement | null>(null)
  const inputId = useId()

  const available = parsers.filter((parser) => {
    // 1. Studio datasetId match
    const studio = readStudio(parser.pipelineJson)?.studio
    if (studio?.datasetId && studio.datasetId === datasetId) {
      return true
    }

    // 2. GCS Prefix match
    const datasetPrefix = dataset?.gcsPrefix?.trim().replace(/^\/+|\/+$/g, "").toLowerCase()
    const parserPrefix = parser.outputGcsPrefix?.trim().replace(/^\/+|\/+$/g, "").toLowerCase()
    if (datasetPrefix && parserPrefix && datasetPrefix === parserPrefix) {
      return true
    }

    // 3. Clean alphanumeric matching on code, name, and prefix
    const cleanCode = code.trim().toLowerCase().replace(/[^a-z0-9]/g, "")
    const cleanParserName = parser.name.toLowerCase().replace(/[^a-z0-9]/g, "")
    const cleanDatasetName = (dataset?.name ?? "").toLowerCase().replace(/[^a-z0-9]/g, "")
    const cleanParserPrefix = (parser.outputGcsPrefix ?? "").toLowerCase().replace(/[^a-z0-9]/g, "")

    if (cleanCode && cleanParserName.includes(cleanCode)) {
      return true
    }
    if (cleanCode && cleanParserPrefix.includes(cleanCode)) {
      return true
    }
    if (cleanDatasetName && cleanParserName.includes(cleanDatasetName)) {
      return true
    }
    if (cleanDatasetName && cleanDatasetName.includes(cleanParserName)) {
      return true
    }

    // 4. Token-based matching (e.g. "SAP_GL" -> ["sap", "gl"])
    const codeTokens = code.toLowerCase().split(/[^a-z0-9]+/).filter(Boolean)
    if (codeTokens.length > 0 && codeTokens.every((token) => cleanParserName.includes(token))) {
      return true
    }

    return false
  })

  const activeParser = available.find((p) => p.isActive) || available[0]

  useEffect(() => {
    if (!selectedParserId && activeParser?.id) {
      onSelectParserId(activeParser.id)
    }
  }, [selectedParserId, activeParser?.id, onSelectParserId])

  const effectiveParserId = selectedParserId || activeParser?.id || ""
  const selectedParser = available.find((item) => item.id === effectiveParserId)

  const runs = useQuery({
    queryKey: ["parser-runs", effectiveParserId],
    queryFn: () => api<ParserRun[]>(`/api/parsers/${effectiveParserId}/runs`),
    enabled: Boolean(effectiveParserId),
  })

  const successfulRuns = (runs.data ?? []).filter((r) => r.status === "Succeeded")
  const selectedRun = runs.data?.find((r) => r.id === selectedRunId)

  function handleFileSelected(file: File) {
    onStageFile(file)
    onSelectRunId("")
  }

  function handleRunSelected(runId: string) {
    onSelectRunId(runId)
    onStageFile(null)
  }

  function handleClear() {
    onStageFile(null)
    onSelectRunId("")
  }

  const isParsing = status === "uploading" || status === "parsing"

  if (available.length === 0) {
    return (
      <div className="flex flex-col justify-between rounded-xl border border-dashed border-border bg-card/50 p-5 shadow-xs">
        <div>
          <div className="flex items-center gap-2 border-b border-border/50 pb-3">
            <span className="rounded bg-muted px-2 py-0.5 font-mono text-xs font-semibold text-foreground">
              {code}
            </span>
            <span className="truncate text-sm font-medium text-foreground">
              {dataset?.name ?? code}
            </span>
          </div>
          <div className="py-6 text-center">
            <p className="text-xs text-muted-foreground">
              No matching parser configured for this dataset.
            </p>
            <p className="mt-1 text-[11px] text-muted-foreground">
              A parser is required to convert raw files into canonical format.
            </p>
          </div>
        </div>
        <div className="pt-2 text-center">
          <Link
            to="/configuration/manual/parsers/new"
            className="inline-flex items-center text-xs font-medium text-primary hover:underline"
          >
            Create parser for {code} &rarr;
          </Link>
        </div>
      </div>
    )
  }

  return (
    <div
      className={cn(
        "flex flex-col rounded-xl border bg-card p-5 shadow-xs transition-all",
        stagedFile
          ? "border-emerald-500/40 shadow-emerald-500/5 ring-1 ring-emerald-500/20"
          : selectedRunId
            ? "border-blue-500/40 shadow-blue-500/5 ring-1 ring-blue-500/20"
            : "border-border/80 hover:border-border"
      )}
    >
      {/* Header: Dataset Code, Name, and Parser selector/badge */}
      <div className="mb-3.5 flex items-center justify-between gap-3 border-b border-border/50 pb-3">
        <div className="flex min-w-0 items-center gap-2">
          <span className="rounded bg-muted px-2 py-0.5 font-mono text-xs font-semibold text-foreground">
            {code}
          </span>
          <span
            className="truncate text-sm font-medium text-foreground"
            title={dataset?.name ?? code}
          >
            {dataset?.name ?? code}
          </span>
        </div>

        {available.length > 1 ? (
          <div className="flex shrink-0 items-center gap-1.5">
            <label htmlFor={`parser-select-${inputId}`} className="text-[11px] text-muted-foreground">
              Parser:
            </label>
            <select
              id={`parser-select-${inputId}`}
              className="h-6 rounded border border-input bg-background px-2 text-xs text-muted-foreground hover:text-foreground focus:outline-none"
              value={effectiveParserId}
              disabled={disabled || isParsing}
              onChange={(e) => onSelectParserId(e.target.value)}
            >
              {available.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name} {item.isActive ? "" : "(draft)"}
                </option>
              ))}
            </select>
          </div>
        ) : selectedParser ? (
          <div className="flex shrink-0 items-center gap-1.5 text-[11px] text-muted-foreground">
            <span
              className={cn(
                "size-1.5 rounded-full",
                selectedParser.isActive ? "bg-emerald-500" : "bg-amber-500"
              )}
            />
            <span className="max-w-[150px] truncate" title={selectedParser.name}>
              {selectedParser.name}
            </span>
          </div>
        ) : null}
      </div>

      {/* Main Content: Staged File, Selected History Run, or Dropzone */}
      <div className="flex-1">
        {stagedFile ? (
          <div className="flex items-center justify-between gap-3 rounded-lg border border-emerald-500/30 bg-emerald-500/5 p-3.5 transition-all">
            <div className="flex min-w-0 items-center gap-3">
              <div className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-emerald-500/10 text-emerald-600 dark:text-emerald-400">
                {getFileIcon(stagedFile.name)}
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <p
                    className="truncate text-sm font-semibold text-foreground"
                    title={stagedFile.name}
                  >
                    {stagedFile.name}
                  </p>
                  <span className="inline-flex shrink-0 items-center rounded-full bg-emerald-500/15 px-2 py-0.5 text-[11px] font-medium text-emerald-700 dark:text-emerald-300">
                    Ready
                  </span>
                </div>
                <p className="mt-0.5 text-xs text-muted-foreground">
                  {formatBytes(stagedFile.size)} · Uploads & parses when you run reconciliation
                </p>
              </div>
            </div>
            {!disabled && !isParsing && (
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                onClick={handleClear}
                className="text-muted-foreground hover:bg-destructive/10 hover:text-destructive"
                title="Remove file"
              >
                <X className="size-4" />
              </Button>
            )}
          </div>
        ) : selectedRunId ? (
          <div className="flex items-center justify-between gap-3 rounded-lg border border-blue-500/30 bg-blue-500/5 p-3.5 transition-all">
            <div className="flex min-w-0 items-center gap-3">
              <div className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-blue-500/10 text-blue-600 dark:text-blue-400">
                <Clock className="size-5" />
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <p
                    className="truncate text-sm font-semibold text-foreground"
                    title={selectedRun?.fileName ?? "Processed file"}
                  >
                    {selectedRun?.fileName ?? `Run ${selectedRunId.slice(0, 8)}…`}
                  </p>
                  <span className="inline-flex shrink-0 items-center rounded-full bg-blue-500/15 px-2 py-0.5 text-[11px] font-medium text-blue-700 dark:text-blue-300">
                    Existing run
                  </span>
                </div>
                <p className="mt-0.5 text-xs text-muted-foreground">
                  {selectedRun ? new Date(selectedRun.creationTime).toLocaleString() : ""} · Ready for reconciliation
                </p>
              </div>
            </div>
            {!disabled && !isParsing && (
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                onClick={handleClear}
                className="text-muted-foreground hover:bg-destructive/10 hover:text-destructive"
                title="Change file"
              >
                <X className="size-4" />
              </Button>
            )}
          </div>
        ) : (
          <div className="space-y-2.5">
            <div
              onDragOver={(e) => {
                e.preventDefault()
                e.stopPropagation()
                if (!disabled && !isParsing) setIsDragging(true)
              }}
              onDragLeave={(e) => {
                e.preventDefault()
                e.stopPropagation()
                setIsDragging(false)
              }}
              onDrop={(e) => {
                e.preventDefault()
                e.stopPropagation()
                setIsDragging(false)
                if (disabled || isParsing) return
                const dropped = e.dataTransfer.files?.[0]
                if (dropped) handleFileSelected(dropped)
              }}
              onClick={() => {
                if (!disabled && !isParsing) fileInputRef.current?.click()
              }}
              className={cn(
                "group flex cursor-pointer flex-col items-center justify-center rounded-lg border-2 border-dashed p-6 text-center transition-all",
                isDragging
                  ? "border-primary bg-primary/5 ring-2 ring-primary/20"
                  : "border-border/80 hover:border-primary/50 hover:bg-muted/30",
                (disabled || isParsing) && "pointer-events-none opacity-60"
              )}
            >
              <input
                ref={fileInputRef}
                type="file"
                className="hidden"
                accept=".csv,.xlsx,.xls,.txt,.tsv,.json"
                disabled={disabled || isParsing}
                onChange={(e) => {
                  const file = e.target.files?.[0]
                  if (file) handleFileSelected(file)
                }}
              />
              <div className="flex size-10 items-center justify-center rounded-full bg-muted text-muted-foreground transition-colors group-hover:bg-primary/10 group-hover:text-primary">
                <UploadCloud className="size-5" />
              </div>
              <div className="mt-2.5">
                <p className="text-xs font-medium text-foreground">
                  <span className="text-primary underline-offset-2 group-hover:underline">
                    Choose a file
                  </span>{" "}
                  or drag & drop
                </p>
                <p className="mt-0.5 text-[11px] text-muted-foreground">
                  CSV, Excel (.xlsx, .xls), TXT, or JSON
                </p>
              </div>
            </div>

            {/* Optional Previous Runs History Toggle */}
            {successfulRuns.length > 0 && (
              <div>
                <button
                  type="button"
                  disabled={disabled || isParsing}
                  onClick={() => setShowHistory((prev) => !prev)}
                  className="inline-flex items-center gap-1.5 text-xs text-muted-foreground transition-colors hover:text-foreground disabled:opacity-50"
                >
                  <Clock className="size-3 text-muted-foreground" />
                  <span>
                    Or choose from {successfulRuns.length} previous run
                    {successfulRuns.length > 1 ? "s" : ""}
                  </span>
                  {showHistory ? (
                    <ChevronUp className="size-3" />
                  ) : (
                    <ChevronDown className="size-3" />
                  )}
                </button>

                {showHistory && (
                  <div className="mt-2">
                    <select
                      className={cn(inputClass, "h-8 rounded-md text-xs")}
                      value=""
                      disabled={disabled || isParsing}
                      onChange={(e) => {
                        if (e.target.value) {
                          handleRunSelected(e.target.value)
                          setShowHistory(false)
                        }
                      }}
                    >
                      <option value="">Select a previous completed run...</option>
                      {successfulRuns.map((r) => (
                        <option key={r.id} value={r.id}>
                          {r.fileName} · {new Date(r.creationTime).toLocaleString()}
                        </option>
                      ))}
                    </select>
                  </div>
                )}
              </div>
            )}
          </div>
        )}
      </div>

      {/* Parsing In-Progress Feedback */}
      {isParsing && (
        <div className="mt-3 flex items-center gap-2.5 rounded-lg border border-amber-500/20 bg-amber-500/10 px-3.5 py-2.5 text-xs text-amber-700 dark:text-amber-400">
          <Loader2 className="size-4 shrink-0 animate-spin" />
          <div className="min-w-0 flex-1">
            <p className="font-medium">Uploading and parsing file...</p>
            <p className="truncate text-[11px] opacity-80">{stagedFile?.name}</p>
          </div>
        </div>
      )}

      {/* Succeeded Feedback */}
      {status === "succeeded" && (
        <div className="mt-3 flex items-center gap-2 rounded-lg border border-emerald-500/20 bg-emerald-500/10 px-3 py-1.5 text-xs text-emerald-700 dark:text-emerald-400">
          <CheckCircle2 className="size-3.5 shrink-0" />
          <span>Parsed successfully. Ready for reconciliation.</span>
        </div>
      )}

      {/* Error Feedback */}
      {error && (
        <div className="mt-3 flex items-start gap-2.5 rounded-lg border border-destructive/20 bg-destructive/10 px-3.5 py-2.5 text-xs text-destructive">
          <AlertCircle className="mt-0.5 size-4 shrink-0" />
          <div className="min-w-0 flex-1">
            <p className="font-semibold">Parsing failed</p>
            <p className="mt-0.5 break-words">{error}</p>
          </div>
        </div>
      )}
    </div>
  )
}
