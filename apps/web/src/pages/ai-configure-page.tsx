import { Button } from "@workspace/ui/components/button"
import {
  AlertCircle,
  Bot,
  CheckCircle2,
  Circle,
  Clock,
  Database,
  FileCode,
  FileText,
  Loader2,
  Play,
  RefreshCw,
  Sparkles,
  Trash2,
  Upload,
} from "lucide-react"
import { useEffect, useState } from "react"
import { Link, useNavigate } from "react-router"
import { ErrorNote } from "@/components/error-note"
import {
  datasetName,
  extractFilesFromZip,
  getAiSessionState,
  isSupportedFile,
  isZipFile,
  resumeAiConfiguration,
  resetAiConfiguration,
  runSampleReconciliation,
  startAiConfiguration,
  subscribeAiSession,
  STORAGE_KEY_CONTEXT,
  type AiSessionState,
} from "@/lib/ai-config-session"
import { inputClass, labelClass, textareaClass } from "@/lib/ui"

type LocalStagedFile = { id: string; file: File }

type LocalDatasetGroup = {
  id: string
  code: string
  name: string
  files: LocalStagedFile[]
}

const DEFAULT_PRESET_CODES = ["POS", "GATEWAY", "BANK", "ERP", "LEDGER"]

function createDefaultGroups(count: number): LocalDatasetGroup[] {
  return Array.from({ length: count }, (_, i) => ({
    id: `group-${i + 1}`,
    code: DEFAULT_PRESET_CODES[i] || `DATASET_${i + 1}`,
    name: DEFAULT_PRESET_CODES[i]
      ? `${DEFAULT_PRESET_CODES[i]} Data`
      : `Dataset ${i + 1}`,
    files: [],
  }))
}

function resizeGroups(
  current: LocalDatasetGroup[],
  targetCount: number
): LocalDatasetGroup[] {
  const clamped = Math.max(2, Math.min(5, targetCount))
  if (current.length === clamped) return current
  if (current.length < clamped) {
    const extra = Array.from({ length: clamped - current.length }, (_, idx) => {
      const i = current.length + idx
      return {
        id: `group-${i + 1}-${Date.now()}`,
        code: DEFAULT_PRESET_CODES[i] || `DATASET_${i + 1}`,
        name: DEFAULT_PRESET_CODES[i]
          ? `${DEFAULT_PRESET_CODES[i]} Data`
          : `Dataset ${i + 1}`,
        files: [],
      }
    })
    return [...current, ...extra]
  }
  return current.slice(0, clamped)
}

const CONFIG_STEPS = [
  {
    id: 1,
    title: "1. Inspect Samples",
    desc: "1 parser per file: delimiters, headers, cleansers & AI anomaly fallback",
  },
  {
    id: 2,
    title: "2. AI Rule Drafting",
    desc: "Gemini analyzes patterns & waterfall match rules",
  },
  {
    id: 3,
    title: "3. Create Datasets",
    desc: "Register canonical schemas per dataset group",
  },
  {
    id: 4,
    title: "4. Create Parsers",
    desc: "Compile pipelines & map files into datasets",
  },
  {
    id: 5,
    title: "5. Run ETL Pipeline",
    desc: "Upload files & parse all records into storage",
  },
  {
    id: 6,
    title: "6. Finalize Profile",
    desc: "Save package & multi-pass reconciliation profile",
  },
] as const

const AI_THOUGHTS = [
  "Analyzing file anatomy, delimiter detection, and trailer total rows…",
  "Monitoring data anomalies: Gemini AI Fallback engine active for self-healing…",
  "Synthesizing dedicated parser pipelines for each uploaded file…",
  "Deriving unified canonical dataset schemas across assigned groups…",
  "Correlating potential cross-dataset match keys and reference IDs…",
  "Formulating waterfall cascade rules with fuzzy edit tolerances…",
  "Evaluating dual-anchor perspectives (reference vs card PAN variations)…",
  "Compiling Polars DSL 2.0 transformations and verifying contracts…",
]

function formatBytes(bytes: number) {
  if (bytes === 0) return "0 B"
  const k = 1024
  const sizes = ["B", "KB", "MB", "GB"]
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`
}

export function AiConfigurePage() {
  const navigate = useNavigate()
  const [session, setSession] = useState<AiSessionState>(getAiSessionState)
  const [datasetCountMode, setDatasetCountMode] = useState<"2" | "3" | "custom">("2")
  const [customCount, setCustomCount] = useState<number>(4)
  const [groups, setGroups] = useState<LocalDatasetGroup[]>(() =>
    createDefaultGroups(2)
  )
  const [context, setContext] = useState<string>(() => {
    try {
      return session.context || localStorage.getItem(STORAGE_KEY_CONTEXT) || ""
    } catch {
      return ""
    }
  })
  const [thoughtIndex, setThoughtIndex] = useState(0)
  const [sampleRunPending, setSampleRunPending] = useState(false)
  const [sampleRunError, setSampleRunError] = useState("")
  const [fileError, setFileError] = useState("")

  // Subscribe to background AI session store
  useEffect(() => {
    return subscribeAiSession((updated) => {
      setSession(updated)
      if (updated.context && !context) {
        setContext(updated.context)
      }
    })
  }, [context])

  // Rotating thought ticker during active execution
  useEffect(() => {
    if (session.status !== "running") return

    const thoughtInterval = setInterval(() => {
      setThoughtIndex((prev) => (prev + 1) % AI_THOUGHTS.length)
    }, 2800)

    return () => clearInterval(thoughtInterval)
  }, [session.status])

  function handleCountModeChange(mode: "2" | "3" | "custom") {
    setDatasetCountMode(mode)
    const target = mode === "2" ? 2 : mode === "3" ? 3 : customCount
    setGroups((prev) => resizeGroups(prev, target))
  }

  function handleCustomCountChange(val: number) {
    const clamped = Math.max(2, Math.min(5, val))
    setCustomCount(clamped)
    if (datasetCountMode === "custom") {
      setGroups((prev) => resizeGroups(prev, clamped))
    }
  }

  async function handleAddFilesToGroup(
    groupId: string,
    fileList: FileList | null
  ) {
    if (!fileList || fileList.length === 0) return
    setFileError("")
    const filesToAdd: LocalStagedFile[] = []

    for (const file of Array.from(fileList)) {
      if (isZipFile(file.name)) {
        try {
          const extracted = await extractFilesFromZip(file)
          if (extracted.length === 0) {
            setFileError(
              `Archive "${file.name}" was skipped (contains no data records or supported files).`
            )
            continue
          }
          for (const extFile of extracted) {
            filesToAdd.push({
              id: `${extFile.name}-${extFile.size}-${extFile.lastModified}-${Math.random().toString(36).slice(2, 7)}`,
              file: extFile,
            })
          }
        } catch (err) {
          setFileError(
            `Failed to extract ZIP archive "${file.name}": ${err instanceof Error ? err.message : "Invalid or corrupt archive"}`
          )
        }
      } else if (isSupportedFile(file.name)) {
        if (file.size === 0) {
          setFileError(`File "${file.name}" was skipped (0 bytes, contains no data).`)
          continue
        }
        filesToAdd.push({
          id: `${file.name}-${file.size}-${file.lastModified}-${Math.random().toString(36).slice(2, 7)}`,
          file,
        })
      } else {
        setFileError(
          `"${file.name}" is not a supported file format. Please upload .csv, .xlsx, .txt, or .zip archives.`
        )
      }
    }

    if (filesToAdd.length === 0) return

    setGroups((prev) =>
      prev.map((g) => {
        if (g.id !== groupId) return g
        let code = g.code
        if (
          g.files.length === 0 &&
          /^DATASET_\d+$/.test(code) &&
          filesToAdd.length > 0
        ) {
          code = datasetName(filesToAdd[0].file.name)
        }
        return {
          ...g,
          code,
          files: [...g.files, ...filesToAdd],
        }
      })
    )
  }

  function handleRemoveFileFromGroup(groupId: string, fileId: string) {
    setGroups((prev) =>
      prev.map((g) =>
        g.id === groupId
          ? { ...g, files: g.files.filter((f) => f.id !== fileId) }
          : g
      )
    )
  }

  function handleGroupCodeChange(groupId: string, newCode: string) {
    const sanitized = newCode
      .toUpperCase()
      .replace(/[^A-Z0-9_]/g, "")
      .slice(0, 32)
    setGroups((prev) =>
      prev.map((g) => (g.id === groupId ? { ...g, code: sanitized } : g))
    )
  }

  function handleContextChange(value: string) {
    setContext(value)
    try {
      localStorage.setItem(STORAGE_KEY_CONTEXT, value)
    } catch {
      // ignore
    }
  }

  // Validation
  const allGroupsHaveFiles = groups.every((g) => g.files.length > 0)
  const distinctCodes = new Set(groups.map((g) => g.code.trim().toUpperCase()))
  const allCodesUnique = distinctCodes.size === groups.length
  const allCodesValid = groups.every((g) =>
    /^[A-Z][A-Z0-9_]{0,31}$/.test(g.code.trim().toUpperCase())
  )
  const canStart =
    groups.length >= 2 &&
    groups.length <= 5 &&
    allGroupsHaveFiles &&
    allCodesUnique &&
    allCodesValid

  async function handleStart() {
    if (!canStart) return
    setSampleRunError("")
    await startAiConfiguration(
      groups.map((g) => ({
        id: g.id,
        code: g.code.trim().toUpperCase(),
        name: g.name || g.code.trim().toUpperCase(),
        files: g.files,
      })),
      context
    )
  }

  async function handleResume() {
    setSampleRunError("")
    await resumeAiConfiguration()
  }

  async function handleReset() {
    setGroups(createDefaultGroups(datasetCountMode === "3" ? 3 : 2))
    setSampleRunError("")
    await resetAiConfiguration()
  }

  async function handleRunSamples() {
    if (!session.completion) return
    setSampleRunPending(true)
    setSampleRunError("")
    try {
      const result = await runSampleReconciliation()
      navigate(`/reconciliation/runs/${result.runId}/grid`)
    } catch (caught) {
      setSampleRunError(
        caught instanceof Error ? caught.message : "The sample run failed."
      )
    } finally {
      setSampleRunPending(false)
    }
  }

  const isOngoing =
    session.status === "running" ||
    session.status === "paused" ||
    (session.status === "error" && session.currentStep > 0)
  const isCompleted =
    session.status === "completed" && Boolean(session.completion)

  // Group active files by dataset code for structured live progress display
  const activeFilesByDataset = session.files.reduce(
    (acc, f) => {
      const dCode = f.datasetCode || "DEFAULT"
      if (!acc[dCode]) acc[dCode] = []
      acc[dCode].push(f)
      return acc
    },
    {} as Record<string, typeof session.files>
  )

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <Button
            variant="link"
            className="h-auto px-0"
            render={<Link to="/configuration" />}
          >
            Configuration
          </Button>
          <div className="flex items-center gap-3">
            <h1 className="text-2xl font-medium">Configure with AI</h1>
            {session.status === "running" ? (
              <span className="flex items-center gap-1.5 rounded-full bg-primary/10 px-2.5 py-0.5 text-xs font-semibold text-primary animate-pulse">
                <Loader2 className="size-3 animate-spin" />
                Workflow in progress
              </span>
            ) : session.status === "paused" ? (
              <span className="flex items-center gap-1.5 rounded-full bg-amber-500/10 px-2.5 py-0.5 text-xs font-semibold text-amber-600 dark:text-amber-400">
                <Clock className="size-3" />
                Workflow paused (Reloaded)
              </span>
            ) : isCompleted ? (
              <span className="flex items-center gap-1.5 rounded-full bg-emerald-500/10 px-2.5 py-0.5 text-xs font-semibold text-emerald-600 dark:text-emerald-400">
                <CheckCircle2 className="size-3" />
                Configuration Ready
              </span>
            ) : null}
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            Divide your datasets and upload multiple files per dataset group (.csv, .xlsx, .txt).
            The AI synthesizes 1 parser per file, registers canonical schemas,
            and drafts multi-pass reconciliation cascades.
          </p>
        </div>

        {isOngoing || isCompleted ? (
          <div className="flex items-center gap-2">
            {session.status === "paused" || session.status === "error" ? (
              <Button
                variant="default"
                size="sm"
                onClick={handleResume}
                className="flex items-center gap-1.5"
              >
                <Play className="size-3.5 fill-current" />
                Resume workflow
              </Button>
            ) : null}
            <Button
              variant="outline"
              size="sm"
              onClick={handleReset}
              className="flex items-center gap-1.5 text-muted-foreground hover:text-foreground"
            >
              <Trash2 className="size-3.5" />
              {isCompleted ? "Start fresh setup" : "Cancel / Start over"}
            </Button>
          </div>
        ) : null}
      </div>

      {/* Stepper Grid & AI Thoughts Banner */}
      {isOngoing || isCompleted ? (
        <article className="flex flex-col gap-3 rounded-lg border bg-card p-4">
          <div className="flex items-center justify-between border-b pb-2">
            <div className="flex items-center gap-2">
              <Bot className="size-4 text-primary" />
              <span className="text-sm font-medium">Configuration Workflow</span>
              {isCompleted ? (
                <span className="rounded-full bg-emerald-500/10 px-2 py-0.5 text-[11px] font-medium text-emerald-600 dark:text-emerald-400">
                  Completed
                </span>
              ) : null}
            </div>
            <span className="text-xs text-muted-foreground">
              {isCompleted
                ? "6 of 6 steps completed"
                : session.currentStep > 0
                ? `Step ${Math.min(session.currentStep, 6)} of 6`
                : "Ready"}
            </span>
          </div>

          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-6">
            {CONFIG_STEPS.map((step) => {
              const isDone =
                isCompleted ||
                session.currentStep > step.id ||
                (session.currentStep === 6 &&
                  !session.error &&
                  session.status !== "running")
              const isCurrent =
                session.currentStep === step.id && session.status === "running"
              const isPaused =
                session.currentStep === step.id && session.status === "paused"
              const isFailed =
                session.currentStep === step.id && session.status === "error"

              return (
                <div
                  key={step.id}
                  className={`flex flex-col gap-1 rounded-md border p-2.5 text-xs transition-colors ${
                    isCurrent
                      ? "border-primary bg-primary/5 text-primary"
                      : isDone
                      ? "border-emerald-500/30 bg-emerald-500/5 text-foreground"
                      : isPaused
                      ? "border-amber-500/40 bg-amber-500/5 text-foreground"
                      : isFailed
                      ? "border-destructive bg-destructive/5 text-destructive"
                      : "border-border/60 bg-muted/20 text-muted-foreground opacity-60"
                  }`}
                >
                  <div className="flex items-center gap-1.5 font-medium">
                    {isCurrent ? (
                      <Loader2 className="size-3.5 animate-spin text-primary shrink-0" />
                    ) : isDone ? (
                      <CheckCircle2 className="size-3.5 text-emerald-500 shrink-0" />
                    ) : isPaused ? (
                      <Clock className="size-3.5 text-amber-500 shrink-0" />
                    ) : isFailed ? (
                      <AlertCircle className="size-3.5 text-destructive shrink-0" />
                    ) : (
                      <Circle className="size-3.5 text-muted-foreground/40 shrink-0" />
                    )}
                    <span className="truncate">{step.title}</span>
                  </div>
                  <p className="line-clamp-2 text-[11px] text-muted-foreground">
                    {step.desc}
                  </p>
                </div>
              )
            })}
          </div>

          {session.stepMessage ? (
            <p className="text-xs font-medium text-foreground/90">
              {session.stepMessage}
            </p>
          ) : null}

          {session.status === "running" ? (
            <div className="flex items-center gap-2.5 rounded-md border border-amber-500/20 bg-amber-500/10 px-3 py-2 text-xs text-amber-900 dark:text-amber-200">
              <Sparkles
                className="size-4 animate-spin text-amber-500 shrink-0"
                style={{ animationDuration: "3s" }}
              />
              <div className="flex flex-1 items-center justify-between gap-2 overflow-hidden">
                <span className="truncate italic transition-all duration-300">
                  {AI_THOUGHTS[thoughtIndex % AI_THOUGHTS.length]}
                </span>
                <span className="shrink-0 font-mono text-[11px] opacity-75">
                  <Clock className="inline mr-1 size-3" />
                  {session.elapsedSeconds}s
                </span>
              </div>
            </div>
          ) : null}

          {/* AI Fallback Decision Engine Callout */}
          {session.latestAiDecision ? (
            <div className="flex flex-col gap-2 rounded-lg border border-cyan-500/30 bg-cyan-500/10 p-3 text-xs text-cyan-950 dark:text-cyan-100 shadow-xs">
              <div className="flex items-center gap-2">
                <Bot className="size-4 text-cyan-600 dark:text-cyan-400 shrink-0" />
                <span className="font-semibold uppercase tracking-wider text-[11px] text-cyan-700 dark:text-cyan-300">
                  Autonomous AI Fallback Decision Engine
                </span>
                <span className="ml-auto rounded-full bg-cyan-500/20 px-2 py-0.5 text-[10px] font-mono font-medium text-cyan-800 dark:text-cyan-200">
                  {session.latestAiDecision.actionTaken}
                </span>
              </div>
              <div className="text-[12px] leading-relaxed">
                <span className="font-semibold text-cyan-900 dark:text-cyan-200">Diagnosis:</span>{" "}
                {session.latestAiDecision.diagnosis}
              </div>
              <div className="text-[12px] leading-relaxed text-cyan-800 dark:text-cyan-100">
                <span className="font-semibold text-cyan-900 dark:text-cyan-200">Action:</span>{" "}
                {session.latestAiDecision.userMessage}
              </div>
            </div>
          ) : null}

          {/* Active Session Files Grouped by Dataset */}
          {session.files.length > 0 ? (
            <div className="mt-1 flex flex-col gap-3 rounded border bg-muted/10 p-3">
              <span className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
                Active Session Pipeline ({session.files.length} files across{" "}
                {Object.keys(activeFilesByDataset).length} datasets)
              </span>
              <div className="flex flex-col gap-3">
                {Object.entries(activeFilesByDataset).map(([dCode, fList]) => {
                  const hasDataset = Boolean(session.artifacts.datasets[dCode])
                  return (
                    <div
                      key={dCode}
                      className="flex flex-col gap-2 rounded border bg-background/50 p-2.5"
                    >
                      <div className="flex items-center justify-between border-b pb-1">
                        <div className="flex items-center gap-1.5">
                          <Database className="size-3.5 text-primary" />
                          <span className="font-mono text-xs font-bold text-foreground">
                            {dCode}
                          </span>
                          <span
                            className={`ml-1.5 px-1.5 py-0.2 rounded text-[10px] ${
                              hasDataset
                                ? "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 font-medium"
                                : "bg-muted text-muted-foreground"
                            }`}
                          >
                            {hasDataset ? "Dataset Registered" : "Dataset Pending"}
                          </span>
                        </div>
                        <span className="text-[11px] text-muted-foreground">
                          {fList.length} {fList.length === 1 ? "file" : "files"}
                        </span>
                      </div>

                      <div className="grid gap-2 sm:grid-cols-2 md:grid-cols-3">
                        {fList.map((file) => {
                          const hasParser = Boolean(
                            session.artifacts.parsers[file.parserCode]
                          )
                          const hasRun = Boolean(
                            session.artifacts.runs[file.id]?.status ===
                              "Succeeded"
                          )

                          return (
                            <div
                              key={file.id}
                              className="flex flex-col gap-1 rounded border bg-card p-2 text-xs shadow-xs"
                            >
                              <div className="flex items-center justify-between gap-1">
                                <span className="font-semibold truncate text-foreground">
                                  {file.name}
                                </span>
                                {file.size > 0 ? (
                                  <span className="font-mono text-[10px] text-muted-foreground shrink-0">
                                    {formatBytes(file.size)}
                                  </span>
                                ) : null}
                              </div>
                              <div className="flex items-center gap-1 font-mono text-[10px] text-muted-foreground">
                                <span>Parser:</span>
                                <strong className="text-foreground truncate">
                                  {file.parserCode}
                                </strong>
                              </div>
                              <div className="mt-1 flex flex-wrap gap-1.5 pt-1 border-t border-muted/40 text-[10px]">
                                <span
                                  className={`flex items-center gap-1 px-1.5 py-0.5 rounded ${
                                    hasParser
                                      ? "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 font-medium"
                                      : "bg-muted text-muted-foreground"
                                  }`}
                                >
                                  <FileCode className="size-2.5" />
                                  Parser
                                </span>
                                <span
                                  className={`flex items-center gap-1 px-1.5 py-0.5 rounded ${
                                    hasRun
                                      ? "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 font-medium"
                                      : "bg-muted text-muted-foreground"
                                  }`}
                                >
                                  <FileText className="size-2.5" />
                                  Parsed
                                </span>
                              </div>
                            </div>
                          )
                        })}
                      </div>
                    </div>
                  )
                })}
              </div>
            </div>
          ) : null}
        </article>
      ) : null}

      {/* Paused Banner */}
      {session.status === "paused" ? (
        <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-amber-500/30 bg-amber-500/10 p-4 text-xs">
          <div className="flex items-center gap-2 text-amber-900 dark:text-amber-200">
            <Clock className="size-4 shrink-0 text-amber-600 dark:text-amber-400" />
            <div>
              <p className="font-medium text-sm">
                Session was paused upon page reload
              </p>
              <p className="text-muted-foreground mt-0.5">
                Your uploaded files and dataset groups are fully preserved. Click
                resume to continue Step {session.currentStep || 1}.
              </p>
            </div>
          </div>
          <Button
            size="sm"
            onClick={handleResume}
            className="flex items-center gap-1.5"
          >
            <Play className="size-3.5 fill-current" />
            Resume configuration
          </Button>
        </div>
      ) : null}

      {/* Completion View */}
      {isCompleted && session.completion ? (
        <article className="flex flex-col gap-4 border bg-card p-5 rounded-lg shadow-sm">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 className="font-medium text-base">
              Configuration ready: {session.completion.package.name}
            </h2>
            <span className="text-xs text-muted-foreground bg-muted px-2 py-0.5 rounded">
              State preserved across sessions
            </span>
          </div>

          <p className="text-sm text-muted-foreground">
            Review and edit the saved configuration, or approve the profile and
            immediately run reconciliation on all uploaded files.
          </p>

          <div className="flex flex-col gap-2 rounded border bg-muted/20 p-3">
            <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
              Processed Datasets & Parsers
            </span>
            <ul className="flex flex-col gap-2 text-sm">
              {session.completion.entries.map((entry) => (
                <li
                  key={entry.parserCode || entry.fileName}
                  className="flex flex-wrap items-center gap-2"
                >
                  <span className="font-medium">{entry.fileName}</span>
                  <span className="text-muted-foreground">→</span>
                  <Link
                    className="underline hover:text-primary"
                    to="/configuration/manual/datasets"
                  >
                    Dataset: {entry.datasetCode}
                  </Link>
                  <span className="text-muted-foreground">·</span>
                  <Link
                    className="underline hover:text-primary"
                    to={`/configuration/manual/parsers/${entry.parserId}`}
                  >
                    Parser: {entry.parserCode}
                  </Link>
                </li>
              ))}
            </ul>
          </div>

          <div className="flex flex-wrap gap-2 pt-2">
            <Button
              variant="outline"
              disabled={sampleRunPending}
              render={
                <Link
                  to={`/reconciliation/profiles/${session.completion.package.profileId}`}
                />
              }
            >
              Review configuration
            </Button>
            <Button
              disabled={sampleRunPending}
              onClick={() => void handleRunSamples()}
            >
              {sampleRunPending ? (
                <>
                  <Loader2 className="mr-1.5 size-4 animate-spin" />
                  Running…
                </>
              ) : (
                "Run uploaded samples"
              )}
            </Button>
            <Button
              variant="ghost"
              disabled={sampleRunPending}
              onClick={handleReset}
              className="text-muted-foreground hover:text-foreground"
            >
              <Trash2 className="mr-1.5 size-3.5" />
              Configure another
            </Button>
          </div>
        </article>
      ) : !isOngoing ? (
        /* Empty / Idle Upload Form with Dataset Count Division & Multi-file support */
        <form
          className="flex flex-col gap-5 border bg-card p-5 rounded-lg shadow-sm"
          onSubmit={(event) => {
            event.preventDefault()
            void handleStart()
          }}
        >
          {/* Dataset Count Selector */}
          <div className="flex flex-col gap-2 border-b pb-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className={labelClass}>Number of Datasets to Reconcile</span>
              <span className="text-[11px] text-muted-foreground">
                Minimum 2, maximum 5
              </span>
            </div>
            <div className="flex flex-wrap items-center gap-2.5">
              <div className="inline-flex rounded-lg border bg-muted/40 p-1">
                <button
                  type="button"
                  onClick={() => handleCountModeChange("2")}
                  className={`px-3 py-1.5 text-xs font-medium rounded-md transition-all ${
                    datasetCountMode === "2"
                      ? "bg-background text-foreground shadow-sm font-semibold"
                      : "text-muted-foreground hover:text-foreground"
                  }`}
                >
                  2 Datasets (Standard Dual)
                </button>
                <button
                  type="button"
                  onClick={() => handleCountModeChange("3")}
                  className={`px-3 py-1.5 text-xs font-medium rounded-md transition-all ${
                    datasetCountMode === "3"
                      ? "bg-background text-foreground shadow-sm font-semibold"
                      : "text-muted-foreground hover:text-foreground"
                  }`}
                >
                  3 Datasets (Tri-Party)
                </button>
                <button
                  type="button"
                  onClick={() => handleCountModeChange("custom")}
                  className={`px-3 py-1.5 text-xs font-medium rounded-md transition-all ${
                    datasetCountMode === "custom"
                      ? "bg-background text-foreground shadow-sm font-semibold"
                      : "text-muted-foreground hover:text-foreground"
                  }`}
                >
                  Custom Count
                </button>
              </div>

              {datasetCountMode === "custom" && (
                <div className="flex items-center gap-1.5 bg-muted/20 px-2.5 py-1 rounded border">
                  <span className="text-xs text-muted-foreground font-medium">
                    Count:
                  </span>
                  <input
                    type="number"
                    min={2}
                    max={5}
                    value={customCount}
                    onChange={(e) =>
                      handleCustomCountChange(parseInt(e.target.value) || 2)
                    }
                    className="h-7 w-14 rounded border bg-background px-1.5 text-xs font-bold text-center"
                  />
                  <span className="text-[11px] text-muted-foreground">
                    (2 to 5)
                  </span>
                </div>
              )}
            </div>
          </div>

          {/* Dataset Groups Division */}
          <div className="flex flex-col gap-3">
            <div className="flex items-center justify-between">
              <span className={labelClass}>
                Dataset Groups & File Uploads (1 Parser per File)
              </span>
              <span className="text-[11px] text-muted-foreground">
                Upload one or multiple CSV, XLSX, TXT, or ZIP files per dataset group
              </span>
            </div>

            {fileError ? <ErrorNote message={fileError} /> : null}

            <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
              {groups.map((group, groupIdx) => (
                <div
                  key={group.id}
                  className="flex flex-col gap-3 rounded-lg border bg-muted/5 p-4 shadow-xs"
                >
                  <div className="flex items-center justify-between border-b pb-2">
                    <div className="flex items-center gap-2">
                      <span className="flex size-5 items-center justify-center rounded-full bg-primary/10 text-[11px] font-bold text-primary">
                        {groupIdx + 1}
                      </span>
                      <span className="text-xs font-semibold uppercase tracking-wider text-foreground">
                        Dataset {groupIdx + 1}
                      </span>
                    </div>
                    <span className="text-[11px] font-mono text-muted-foreground">
                      {group.files.length}{" "}
                      {group.files.length === 1 ? "file" : "files"}
                    </span>
                  </div>

                  {/* Dataset Code Input */}
                  <label className="flex flex-col gap-1">
                    <span className="text-[11px] font-medium text-foreground/80">
                      Dataset Code <span className="text-destructive">*</span>
                    </span>
                    <input
                      type="text"
                      className={inputClass}
                      placeholder="e.g. POS"
                      value={group.code}
                      onChange={(e) =>
                        handleGroupCodeChange(group.id, e.target.value)
                      }
                    />
                  </label>

                  {/* Upload Dropzone / Button for this Dataset Group */}
                  <div className="flex flex-col gap-2">
                    <label
                      className="flex flex-col items-center justify-center rounded-md border border-dashed border-muted-foreground/30 bg-background/50 p-3 hover:bg-background/80 cursor-pointer transition-colors text-center"
                      onDragOver={(e) => e.preventDefault()}
                      onDrop={(e) => {
                        e.preventDefault()
                        void handleAddFilesToGroup(group.id, e.dataTransfer.files)
                      }}
                    >
                      <Upload className="size-4 text-primary mb-1" />
                      <span className="text-xs font-medium text-foreground">
                        Upload files for {group.code || `Dataset ${groupIdx + 1}`}
                      </span>
                      <span className="text-[10px] text-muted-foreground">
                        Supports .csv, .xlsx, .txt, .zip
                      </span>
                      <input
                        type="file"
                        accept=".csv,.txt,.xlsx,.xls,.zip,text/csv,text/plain,application/zip,application/x-zip-compressed,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/vnd.ms-excel"
                        multiple
                        className="hidden"
                        onChange={(e) => {
                          void handleAddFilesToGroup(group.id, e.target.files)
                          e.target.value = ""
                        }}
                      />
                    </label>

                    {/* Uploaded File List */}
                    {group.files.length > 0 ? (
                      <ul className="flex flex-col gap-1.5 max-h-44 overflow-y-auto">
                        {group.files.map((f) => (
                          <li
                            key={f.id}
                            className="flex items-center justify-between rounded border bg-background px-2.5 py-1.5 text-xs shadow-xs"
                          >
                            <div className="flex items-center gap-1.5 min-w-0">
                              <FileText className="size-3.5 text-muted-foreground shrink-0" />
                              <span className="truncate font-medium">
                                {f.file.name}
                              </span>
                            </div>
                            <div className="flex items-center gap-2 shrink-0">
                              <span className="text-[10px] font-mono text-muted-foreground">
                                {formatBytes(f.file.size)}
                              </span>
                              <button
                                type="button"
                                onClick={() =>
                                  handleRemoveFileFromGroup(group.id, f.id)
                                }
                                className="text-muted-foreground hover:text-destructive p-0.5"
                                title="Remove file"
                              >
                                <Trash2 className="size-3" />
                              </button>
                            </div>
                          </li>
                        ))}
                      </ul>
                    ) : (
                      <p className="text-[11px] text-amber-600 dark:text-amber-400 italic">
                        Upload at least one CSV, XLSX, TXT, or ZIP file.
                      </p>
                    )}
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Business Context */}
          <label className="flex flex-col gap-1.5">
            <div className="flex items-center justify-between">
              <span className={labelClass}>Business context (optional)</span>
              <span className="text-[11px] text-muted-foreground">
                Saved automatically
              </span>
            </div>
            <textarea
              className={textareaClass}
              value={context}
              placeholder="Describe the relationship between datasets, matching criteria, tolerance, or primary keys."
              onChange={(event) => handleContextChange(event.target.value)}
            />
          </label>

          {/* Action Button & Validation Messages */}
          <div className="flex flex-col gap-2">
            {!allCodesValid ? (
              <p className="text-xs text-destructive">
                Dataset codes must start with a letter and contain only letters,
                numbers, and underscores (max 32 characters).
              </p>
            ) : !allCodesUnique ? (
              <p className="text-xs text-destructive">
                Each dataset group must have a unique code.
              </p>
            ) : !allGroupsHaveFiles ? (
              <p className="text-xs text-muted-foreground">
                Please attach at least one file (.csv, .xlsx, or .txt) to each
                dataset group before continuing.
              </p>
            ) : null}

            <div>
              <Button
                type="submit"
                disabled={!canStart}
                className="flex items-center gap-2"
              >
                <Bot className="size-4" />
                Configure from dataset groups
              </Button>
            </div>
          </div>
        </form>
      ) : null}

      {session.error ? <ErrorNote message={session.error} /> : null}
      {sampleRunError ? <ErrorNote message={sampleRunError} /> : null}

      {session.statusText &&
      session.status !== "error" &&
      session.status !== "idle" ? (
        <p className="text-sm text-muted-foreground" role="status">
          {session.statusText}
        </p>
      ) : null}

      {session.status === "error" && (
        <div className="flex flex-wrap items-center gap-3 pt-1">
          <Button
            size="sm"
            onClick={handleResume}
            className="flex items-center gap-1.5"
          >
            <RefreshCw className="size-3.5" />
            Retry / Resume setup
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={handleReset}
            className="text-muted-foreground"
          >
            Start fresh setup
          </Button>
        </div>
      )}
    </section>
  )
}
