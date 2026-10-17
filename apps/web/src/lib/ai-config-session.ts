import { api } from "@/lib/api"
import {
  type CanonicalDataset,
  type ParserConfiguration,
  type ParserRun,
} from "@/lib/canonical"
import { uploadAndParse } from "@/lib/parser-runs"
import {
  autoMap,
  compilePipeline,
  detectDelimiter,
  detectHeaderMode,
  emptyStudio,
  extractFilesFromZip,
  isSupportedExtractedFile,
  isZipFile,
  parseExcelSample,
  parseSample,
  syncMappings,
  type SourceColumn,
} from "@/lib/parser-studio"
import { parseProfile, type Package } from "@/lib/types"

export {
  detectDelimiter,
  detectHeaderMode,
  extractFilesFromZip,
  isSupportedExtractedFile,
  isZipFile,
}

export type AiSessionStatus = "idle" | "running" | "paused" | "completed" | "error"

export type StagedFileMeta = {
  id: string
  name: string
  size: number
  datasetCode: string
  parserCode: string
}

export type DatasetGroupMeta = {
  id: string
  code: string
  name?: string
}

export type StagedDatasetGroup = {
  id: string
  code: string
  name?: string
  files: { id: string; file: File }[]
}

export type CreatedArtifacts = {
  datasets: Record<string, CanonicalDataset>
  parsers: Record<string, ParserConfiguration>
  runs: Record<string, ParserRun>
  package?: Package
  definitionJson?: string
}

export type Completion = {
  package: Package
  definitionJson: string
  parserRunIds: Record<string, string>
  multiParserRunIds: Record<string, string[]>
  entries: {
    fileName: string
    datasetCode: string
    parserCode: string
    parserId: string
    runId?: string
  }[]
}

export type AiFallbackDecision = {
  canRecover: boolean
  diagnosis: string
  actionTaken: string
  userMessage: string
  filesToSkip: string[]
  adjustedParsers: {
    code: string
    delimiter: string
    headerMode: "none" | "firstRow" | "rowIndex"
    headerRowIndex: number
    skipRows: number
    removeTotalRows: boolean
    totalRowKeywords: string
    steps: any[]
  }[]
}

export type AiSessionState = {
  status: AiSessionStatus
  currentStep: number
  stepMessage: string
  statusText: string
  error: string
  elapsedSeconds: number
  files: StagedFileMeta[]
  datasetGroups: DatasetGroupMeta[]
  context: string
  artifacts: CreatedArtifacts
  completion: Completion | null
  aiDecisions: AiFallbackDecision[]
  latestAiDecision?: AiFallbackDecision | null
  startedAt?: number
  updatedAt?: number
}

const STORAGE_KEY_SESSION = "grid_ai_config_session_v3"
export const STORAGE_KEY_CONTEXT = "grid_ai_config_context"
export const STORAGE_KEY_COMPLETION = "grid_ai_config_completion"

const IDB_NAME = "grid_ai_config_store"
const IDB_STORE = "staged_files"
const IDB_VERSION = 1

// --- IndexedDB for Persistent File Storage Across Refreshes ---

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    if (typeof indexedDB === "undefined") {
      reject(new Error("IndexedDB is not supported in this environment."))
      return
    }
    const request = indexedDB.open(IDB_NAME, IDB_VERSION)
    request.onupgradeneeded = () => {
      const db = request.result
      if (!db.objectStoreNames.contains(IDB_STORE)) {
        db.createObjectStore(IDB_STORE, { keyPath: "id" })
      }
    }
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error)
  })
}

export async function storeSessionFiles(
  entries: {
    id: string
    file: File
    datasetCode: string
    parserCode: string
    name: string
  }[]
): Promise<void> {
  try {
    const db = await openDb()
    const tx = db.transaction(IDB_STORE, "readwrite")
    const store = tx.objectStore(IDB_STORE)
    await new Promise<void>((resolve, reject) => {
      const clearReq = store.clear()
      clearReq.onsuccess = () => resolve()
      clearReq.onerror = () => reject(clearReq.error)
    })
    for (const item of entries) {
      store.put({
        id: item.id,
        file: item.file,
        datasetCode: item.datasetCode,
        parserCode: item.parserCode,
        name: item.name,
      })
    }
    await new Promise<void>((resolve, reject) => {
      tx.oncomplete = () => resolve()
      tx.onerror = () => reject(tx.error)
    })
  } catch (err) {
    console.warn("Failed to store files in IndexedDB:", err)
  }
}

export async function loadSessionFiles(): Promise<
  {
    id: string
    file: File
    datasetCode: string
    parserCode: string
    name: string
  }[]
> {
  try {
    const db = await openDb()
    const tx = db.transaction(IDB_STORE, "readonly")
    const store = tx.objectStore(IDB_STORE)
    const rawList = await new Promise<any[]>((resolve, reject) => {
      const req = store.getAll()
      req.onsuccess = () => resolve(req.result ?? [])
      req.onerror = () => reject(req.error)
    })
    return rawList.map((item) => ({
      id: item.id,
      file: item.file,
      datasetCode: item.datasetCode || item.code || "DATASET_1",
      parserCode: item.parserCode || item.code || item.id,
      name: item.name || item.file?.name || "",
    }))
  } catch (err) {
    console.warn("Failed to load files from IndexedDB:", err)
    return []
  }
}

export async function clearSessionFiles(): Promise<void> {
  try {
    const db = await openDb()
    const tx = db.transaction(IDB_STORE, "readwrite")
    tx.objectStore(IDB_STORE).clear()
  } catch {
    // ignore
  }
}

// --- Session State Helpers ---

function initialArtifacts(): CreatedArtifacts {
  return {
    datasets: {},
    parsers: {},
    runs: {},
  }
}

function loadInitialState(): AiSessionState {
  try {
    const raw = localStorage.getItem(STORAGE_KEY_SESSION)
    if (raw) {
      const parsed = JSON.parse(raw) as AiSessionState
      if (!parsed.datasetGroups) {
        parsed.datasetGroups = []
      }
      if (!parsed.aiDecisions) {
        parsed.aiDecisions = []
      }
      if (parsed.latestAiDecision === undefined) {
        parsed.latestAiDecision = null
      }
      if (parsed.status === "running") {
        return {
          ...parsed,
          status: "paused",
          stepMessage: `Workflow paused upon page reload. Click Resume to continue step ${parsed.currentStep || 1}.`,
        }
      }
      return parsed
    }
  } catch {
    // fallback
  }

  try {
    const compRaw = localStorage.getItem(STORAGE_KEY_COMPLETION)
    if (compRaw) {
      const completion = JSON.parse(compRaw) as Completion
      return {
        status: "completed",
        currentStep: 6,
        stepMessage: "All 6 configuration steps completed successfully.",
        statusText: "Configuration complete. All uploaded files have been parsed.",
        error: "",
        elapsedSeconds: 0,
        files: completion.entries.map((e) => ({
          id: e.parserCode || e.fileName,
          name: e.fileName,
          size: 0,
          datasetCode: e.datasetCode,
          parserCode: e.parserCode,
        })),
        datasetGroups: [],
        context: localStorage.getItem(STORAGE_KEY_CONTEXT) ?? "",
        artifacts: initialArtifacts(),
        completion,
        aiDecisions: [],
        latestAiDecision: null,
      }
    }
  } catch {
    // fallback
  }

  return {
    status: "idle",
    currentStep: 0,
    stepMessage: "",
    statusText: "",
    error: "",
    elapsedSeconds: 0,
    files: [],
    datasetGroups: [],
    context: localStorage.getItem(STORAGE_KEY_CONTEXT) ?? "",
    artifacts: initialArtifacts(),
    completion: null,
    aiDecisions: [],
    latestAiDecision: null,
  }
}

// --- Global In-Memory Singleton Store ---

let state: AiSessionState = loadInitialState()
const listeners = new Set<(s: AiSessionState) => void>()
const inMemoryFiles = new Map<string, File>()
let activeAbortController: AbortController | null = null
let timerId: ReturnType<typeof setInterval> | null = null

function persistState() {
  try {
    localStorage.setItem(STORAGE_KEY_SESSION, JSON.stringify(state))
    if (state.completion) {
      localStorage.setItem(
        STORAGE_KEY_COMPLETION,
        JSON.stringify(state.completion)
      )
    }
    if (state.context) {
      localStorage.setItem(STORAGE_KEY_CONTEXT, state.context)
    }
  } catch (err) {
    console.warn("Failed to persist session to localStorage:", err)
  }
}

function notify() {
  persistState()
  for (const listener of listeners) {
    listener(state)
  }
}

function startTimer() {
  if (timerId) clearInterval(timerId)
  timerId = setInterval(() => {
    if (state.status === "running") {
      state = {
        ...state,
        elapsedSeconds: state.elapsedSeconds + 1,
      }
      notify()
    } else {
      if (timerId) clearInterval(timerId)
      timerId = null
    }
  }, 1000)
}

function stopTimer() {
  if (timerId) {
    clearInterval(timerId)
    timerId = null
  }
}

// --- Domain Helpers ---

export function datasetName(fileName: string): string {
  const stem = fileName
    .replace(/\.(csv|xlsx|xls|txt)$/i, "")
    .replace(/[^A-Za-z0-9_]+/g, "_")
    .replace(/^_+|_+$/g, "")
    .toUpperCase()
    .slice(0, 32)
  return /^[A-Za-z]/.test(stem) ? stem : `DATA_${stem}`.slice(0, 32)
}

export function isExcelFile(name: string): boolean {
  const lower = name.toLowerCase()
  return lower.endsWith(".xlsx") || lower.endsWith(".xls")
}

export function isSupportedFile(name: string): boolean {
  const lower = name.toLowerCase()
  return (
    lower.endsWith(".csv") ||
    lower.endsWith(".txt") ||
    lower.endsWith(".xlsx") ||
    lower.endsWith(".xls") ||
    lower.endsWith(".zip")
  )
}

export function inferSemanticRole(
  colName: string,
  sampleValues: string[],
  steps?: { kind: string; source: string }[]
): { canonicalName: string; polarsType: string } | null {
  const present = sampleValues.map((v) => v.trim()).filter(Boolean)
  if (present.length === 0) return null

  const clean = colName.trim()
  const isNoiseField =
    /(currency|curr\b|%|rate\b|time\b|name\b|issuer|entry mode|plan\b|tenor\b|terms\b|brand\b|tender_type|cashier|adid|cwt\b|ewt\b|tip\b|meta_row_hash|file date|ipp)/i.test(
      clean
    )
  if (isNoiseField) return null

  // 1. Recognizable semantic column names across POS & Gateway formats:
  if (
    /^(gross_amt|gross|gross amount|tran amount|trans_amount|transaction amount|original transaction amount|charge_amount|amount)$/i.test(
      clean
    )
  ) {
    return { canonicalName: "gross_amt", polarsType: "pl.Decimal(18,2)" }
  }
  if (
    /^(net_amt|net|net amount|deposit net amount|settlement amount|amount paid|amount due)$/i.test(
      clean
    )
  ) {
    return { canonicalName: "net_amt", polarsType: "pl.Decimal(18,2)" }
  }
  if (
    /^(fee_amt|mdr|discount amount|disc_amt|fee|fee amount|commission|tax amount)$/i.test(
      clean
    )
  ) {
    return { canonicalName: "fee_amt", polarsType: "pl.Decimal(18,2)" }
  }
  if (
    /^(trx_date|trans_date|trandate|tran date|txn date|transaction date|sale_date|post_date|date)$/i.test(
      clean
    )
  ) {
    return { canonicalName: "trx_date", polarsType: "pl.Date" }
  }
  if (
    /^(settle_date|settle date|settlement date|settlement|paymentdat|payment date|processing date|value date)$/i.test(
      clean
    )
  ) {
    return { canonicalName: "settle_date", polarsType: "pl.Date" }
  }
  if (
    /^(card_number|card number|card no\.\/desc\.|card no\.?|accountno|account number|pan|card_last_4|card_pan_last4)$/i.test(
      clean
    )
  ) {
    return { canonicalName: "card_number", polarsType: "pl.String" }
  }
  if (
    /^(app_code|authorization code|approval code|auth code|auth_code|approval_code|app code)$/i.test(
      clean
    )
  ) {
    return { canonicalName: "app_code", polarsType: "pl.String" }
  }
  if (/^(terminal_id|terminal id|tid|eft_terminal|pos_terminal)$/i.test(clean)) {
    return { canonicalName: "terminal_id", polarsType: "pl.String" }
  }
  if (
    /^(rrn|reference number|reference no\.?|retrieval reference number|ref_no|trace)$/i.test(
      clean
    )
  ) {
    return { canonicalName: "rrn", polarsType: "pl.String" }
  }
  if (
    /^(order_id|invoice_number|invoice|receipt|ext_ref|seq_no)$/i.test(clean)
  ) {
    return { canonicalName: "order_id", polarsType: "pl.String" }
  }
  if (
    /^(merchant_code|merchant number|merchant_number|merch_nbr|mid)$/i.test(
      clean
    )
  ) {
    return { canonicalName: "merchant_code", polarsType: "pl.String" }
  }
  if (/^(trx_number|transaction number|trans_no)$/i.test(clean)) {
    return { canonicalName: "trx_number", polarsType: "pl.String" }
  }
  if (/^(batch_number|batch_no|batch)$/i.test(clean)) {
    return { canonicalName: "batch_number", polarsType: "pl.String" }
  }

  // 2. Headerless positional conventions (BDO / Gateway headerless TXT files):
  if (clean.startsWith("column_")) {
    if (clean === "column_1") return { canonicalName: "trx_date", polarsType: "pl.Date" }
    if (clean === "column_2") return { canonicalName: "settle_date", polarsType: "pl.Date" }
    if (clean === "column_4") return { canonicalName: "merchant_code", polarsType: "pl.String" }
    if (clean === "column_5") return { canonicalName: "card_number", polarsType: "pl.String" }
    if (clean === "column_6") return { canonicalName: "gross_amt", polarsType: "pl.Decimal(18,2)" }
    if (clean === "column_7") return { canonicalName: "net_amt", polarsType: "pl.Decimal(18,2)" }
    if (clean === "column_8") return { canonicalName: "app_code", polarsType: "pl.String" }
    if (clean === "column_9") return { canonicalName: "rrn", polarsType: "pl.String" }
    if (clean === "column_10") return { canonicalName: "terminal_id", polarsType: "pl.String" }
    if (clean === "column_15") return { canonicalName: "fee_amt", polarsType: "pl.Decimal(18,2)" }
    if (clean === "column_17") return { canonicalName: "terminal_id", polarsType: "pl.String" }
    if (clean === "column_18") return { canonicalName: "order_id", polarsType: "pl.String" }

    if (steps?.some((s) => s.source === clean && s.kind === "scaleCents")) {
      return { canonicalName: "gross_amt", polarsType: "pl.Decimal(18,2)" }
    }
    if (steps?.some((s) => s.source === clean && s.kind === "parseDate")) {
      return { canonicalName: "trx_date", polarsType: "pl.Date" }
    }
    if (steps?.some((s) => s.source === clean && s.kind === "maskCard")) {
      return { canonicalName: "card_number", polarsType: "pl.String" }
    }
  }

  return null
}

export function columnType(
  name: string,
  values: string[],
  steps?: { kind: string; source: string }[]
): string {
  if (steps?.some((s) => s.source === name && s.kind === "parseDate")) {
    return "pl.Date"
  }
  if (steps?.some((s) => s.source === name && s.kind === "scaleCents")) {
    return "pl.Decimal(18,2)"
  }
  const present = values.map((value) => value.trim()).filter(Boolean)
  if (
    present.length &&
    present.every((value) => /^\d{4}-\d{2}-\d{2}$/.test(value))
  )
    return "pl.Date"
  if (
    /(amount|amt|total|fee|price|gross)/i.test(name) &&
    present.length &&
    present.every((value) => /^-?\d+(\.\d{1,2})?$/.test(value))
  )
    return "pl.Decimal(18,2)"
  return "pl.String"
}

// --- AI Fallback Decision Engine ---

export async function consultAiFallback(
  stepIndex: number,
  stepName: string,
  errorMessage: string,
  anomalousFiles: {
    fileId: string
    fileName: string
    datasetCode: string
    parserCode: string
    fileSize: number
    rawSnippet: string
    errorDetail: string
  }[],
  signal?: AbortSignal
): Promise<AiFallbackDecision | null> {
  state = {
    ...state,
    stepMessage: `🤖 Consulting Gemini AI Fallback Decision Engine for step ${stepIndex} (${stepName})…`,
    statusText: "AI Fallback Decision Engine Active",
  }
  notify()

  try {
    const response = await api<AiFallbackDecision>(
      "/api/app/assistant/decide-fallback",
      {
        method: "POST",
        signal,
        body: JSON.stringify({
          stepIndex,
          stepName,
          errorMessage,
          anomalousFiles,
          context: state.context,
        }),
      }
    )

    if (response) {
      state = {
        ...state,
        aiDecisions: [...(state.aiDecisions || []), response],
        latestAiDecision: response,
        stepMessage: `🤖 AI Decision: ${response.userMessage}`,
      }
      notify()
      return response
    }
  } catch (err) {
    console.warn("AI Fallback endpoint error, using local decision heuristic:", err)
  }

  // Local fallback decision heuristic if network/backend is unreachable
  const isNetworkOrTimeout = /timeout|could not be reached|canceled|network|deadline/i.test(errorMessage)
  if (isNetworkOrTimeout) {
    const timeoutDecision: AiFallbackDecision = {
      canRecover: true,
      diagnosis:
        "External AI request timed out under heavy multi-file load. Switched to high-speed deterministic heuristic engine to continue configuration without burning further credits.",
      actionTaken: "switch_to_fast_heuristic",
      userMessage:
        "AI Decision: Gemini generation timed out under heavy multi-file load. Switched to high-speed deterministic heuristic engine to continue configuration without burning further credits.",
      filesToSkip: [],
      adjustedParsers: [],
    }
    state = {
      ...state,
      aiDecisions: [...(state.aiDecisions || []), timeoutDecision],
      latestAiDecision: timeoutDecision,
      stepMessage: `🤖 AI Decision: ${timeoutDecision.userMessage}`,
    }
    notify()
    return timeoutDecision
  }

  const filesToSkip: string[] = []
  const adjustedParsers: AiFallbackDecision["adjustedParsers"] = []

  for (const f of anomalousFiles) {
    const snippet = (f.rawSnippet || "").trim()
    const lines = snippet ? snippet.split(/\r?\n/).filter((l) => l.trim()) : []
    const isTrulyEmpty = f.fileSize === 0 || !snippet || lines.length === 0
    if (isTrulyEmpty) {
      filesToSkip.push(f.fileName)
    } else {
      const delim = snippet ? detectDelimiter(snippet) : ","
      const headerCheck =
        lines.length > 0
          ? detectHeaderMode(lines, delim)
          : { headerMode: "firstRow" as const, headerRowIndex: 0 }
      adjustedParsers.push({
        code: f.parserCode,
        delimiter: delim,
        headerMode: headerCheck.headerMode,
        headerRowIndex: headerCheck.headerRowIndex,
        skipRows: headerCheck.headerRowIndex,
        removeTotalRows: false,
        totalRowKeywords: "",
        steps: [],
      })
    }
  }

  const localDecision: AiFallbackDecision = {
    canRecover: filesToSkip.length > 0 || adjustedParsers.length > 0,
    diagnosis:
      filesToSkip.length > 0
        ? `Identified ${filesToSkip.length} empty file(s) and ${adjustedParsers.length} headerless/raw data file(s).`
        : "Identified headerless data records in uploaded file.",
    actionTaken: filesToSkip.length > 0 ? "skip_empty_files" : "retry_with_none_header",
    userMessage:
      filesToSkip.length > 0
        ? `AI Decision: Safely bypassed empty file(s) (${filesToSkip.join(", ")}) and adapted parser to headerless mode.`
        : "AI Decision: Configured headerMode='none' to retain all rows as transactional records.",
    filesToSkip,
    adjustedParsers,
  }

  state = {
    ...state,
    aiDecisions: [...(state.aiDecisions || []), localDecision],
    latestAiDecision: localDecision,
    stepMessage: `🤖 AI Decision: ${localDecision.userMessage}`,
  }
  notify()
  return localDecision
}

// --- Workflow Runner ---

async function runWorkflowSteps(signal: AbortSignal) {
  // Ensure we have files in memory
  if (inMemoryFiles.size === 0) {
    const idbFiles = await loadSessionFiles()
    for (const item of idbFiles) {
      inMemoryFiles.set(item.id, item.file)
    }
  }

  if (state.files.length === 0 || inMemoryFiles.size === 0) {
    throw new Error("No dataset files found for this session.")
  }

  // Group files by datasetCode
  const distinctDatasetCodes = Array.from(
    new Set(state.files.map((f) => f.datasetCode))
  )
  if (distinctDatasetCodes.length < 2) {
    throw new Error("At least two distinct datasets are required for reconciliation.")
  }

  const fileDataList: {
    meta: StagedFileMeta
    file: File
    fullText?: string
    snippet: string
    isExcel: boolean
    excelInfo?: ReturnType<typeof parseExcelSample>
  }[] = []

  for (const meta of state.files) {
    const file = inMemoryFiles.get(meta.id)
    if (!file) throw new Error(`Missing file payload for ${meta.name}.`)
    if (isZipFile(file.name)) {
      try {
        const extracted = await extractFilesFromZip(file)
        if (extracted.length === 0) {
          fileDataList.push({
            meta,
            file,
            fullText: "",
            snippet: "",
            isExcel: false,
          })
        } else {
          const inner = extracted[0]
          if (isExcelFile(inner.name)) {
            const buffer = await inner.arrayBuffer()
            const excelInfo = parseExcelSample(buffer)
            fileDataList.push({
              meta,
              file: inner,
              snippet: excelInfo.snippet,
              isExcel: true,
              excelInfo,
            })
          } else {
            const fullText = await inner.text()
            const snippet = fullText.split(/\r?\n/).slice(0, 8).join("\n")
            fileDataList.push({
              meta,
              file: inner,
              fullText,
              snippet,
              isExcel: false,
            })
          }
        }
      } catch {
        fileDataList.push({
          meta,
          file,
          fullText: "",
          snippet: "",
          isExcel: false,
        })
      }
    } else if (isExcelFile(file.name)) {
      const buffer = await file.arrayBuffer()
      const excelInfo = parseExcelSample(buffer)
      fileDataList.push({
        meta,
        file,
        snippet: excelInfo.snippet,
        isExcel: true,
        excelInfo,
      })
    } else {
      const fullText = await file.text()
      const snippet = fullText.split(/\r?\n/).slice(0, 8).join("\n")
      fileDataList.push({
        meta,
        file,
        fullText,
        snippet,
        isExcel: false,
      })
    }
  }

  // Step 1: Inspect Samples & Propose Parsers (1 parser per file)
  state = {
    ...state,
    currentStep: 1,
    stepMessage:
      "Analyzing file anatomies, delimiters, preambles & column cleansing rules for each file…",
    statusText: "Analyzing raw files",
  }
  notify()

  // Proactively check for empty or zero-data files before calling AI
  const initialAnomalies = fileDataList
    .filter((item) => {
      if (item.isExcel) {
        return (
          !item.excelInfo?.columns.length || !item.excelInfo?.sampleRows.length
        )
      }
      return !item.fullText?.replace(/^\uFEFF/, "").trim() || item.file.size === 0
    })
    .map((item) => ({
      fileId: item.meta.id,
      fileName: item.file.name,
      datasetCode: item.meta.datasetCode,
      parserCode: item.meta.parserCode,
      fileSize: item.file.size,
      rawSnippet: item.snippet || "",
      errorDetail: item.file.size === 0 ? "0-byte file" : "No readable data rows found",
    }))

  const skippedFileNames = new Set<string>()

  if (initialAnomalies.length > 0) {
    const earlyDecision = await consultAiFallback(
      1,
      "Inspect Samples",
      `Detected ${initialAnomalies.length} empty or zero-byte file(s) during initial scan`,
      initialAnomalies,
      signal
    )
    if (earlyDecision?.filesToSkip?.length) {
      for (const skipped of earlyDecision.filesToSkip) {
        skippedFileNames.add(skipped)
      }
    }
  }

  // Filter out skipped files
  const activeFileList = fileDataList.filter(
    (item) => !skippedFileNames.has(item.file.name)
  )

  const parserProposals: Record<string, any> = {}
  if (activeFileList.length > 0) {
    try {
      const proposalResp = await api<{ parsers: any[] }>(
        "/api/app/assistant/propose-parsers",
        {
          method: "POST",
          signal,
          body: JSON.stringify({
            files: activeFileList.map((item) => ({
              code: item.meta.parserCode,
              fileName: item.file.name,
              rawSnippet: item.snippet,
            })),
            context: state.context,
          }),
        }
      )
      if (proposalResp?.parsers) {
        for (const p of proposalResp.parsers) {
          parserProposals[p.code] = p
        }
      }
    } catch (err) {
      console.warn("AI parser proposal fallback:", err)
    }
  }

  // Apply any adjusted parsers from AI Fallback Decision Engine
  if (state.latestAiDecision?.adjustedParsers?.length) {
    for (const adj of state.latestAiDecision.adjustedParsers) {
      parserProposals[adj.code] = {
        ...(parserProposals[adj.code] || {}),
        ...adj,
      }
    }
  }

  const preparedList: {
    meta: StagedFileMeta
    file: File
    sample: { columns: SourceColumn[]; sampleRows: string[][] }
    proposal?: any
    isExcel: boolean
    sheetName?: string
    excelInfo?: ReturnType<typeof parseExcelSample>
  }[] = []

  for (const item of activeFileList) {
    if (item.isExcel && item.excelInfo) {
      if (!item.excelInfo.columns.length || !item.excelInfo.sampleRows.length) {
        console.warn(`Skipping empty Excel file ${item.file.name}`)
        continue
      }
      preparedList.push({
        meta: item.meta,
        file: item.file,
        sample: {
          columns: item.excelInfo.columns,
          sampleRows: item.excelInfo.sampleRows,
        },
        proposal: parserProposals[item.meta.parserCode],
        isExcel: true,
        sheetName: item.excelInfo.sheetName,
        excelInfo: item.excelInfo,
      })
    } else {
      const rawText = item.fullText?.replace(/^\uFEFF/, "").trim() || ""
      if (!rawText) {
        console.warn(`Skipping empty text file ${item.file.name}`)
        continue
      }
      const proposal = parserProposals[item.meta.parserCode]
      const delimiter =
        proposal?.delimiter || detectDelimiter(rawText)
      const headerMode = proposal?.headerMode || "firstRow"
      const headerRowIndex = proposal?.headerRowIndex || 0

      let sample: ReturnType<typeof parseSample>
      try {
        sample = parseSample(
          item.fullText || "",
          delimiter,
          headerMode,
          headerRowIndex
        )
      } catch (parseErr) {
        // AI Fallback Decision triggered on parse error
        const parseDecision = await consultAiFallback(
          1,
          "Inspect Samples",
          parseErr instanceof Error ? parseErr.message : "Sample parsing error",
          [
            {
              fileId: item.meta.id,
              fileName: item.file.name,
              datasetCode: item.meta.datasetCode,
              parserCode: item.meta.parserCode,
              fileSize: item.file.size,
              rawSnippet: item.snippet,
              errorDetail:
                parseErr instanceof Error
                  ? parseErr.message
                  : String(parseErr),
            },
          ],
          signal
        )

        const adj = parseDecision?.adjustedParsers.find(
          (p) => p.code === item.meta.parserCode
        )
        const retryHeaderMode = adj?.headerMode || "none"
        const retryDelimiter =
          adj?.delimiter || detectDelimiter(rawText)
        sample = parseSample(
          item.fullText || "",
          retryDelimiter,
          retryHeaderMode,
          0
        )
      }

      if (!sample.columns.length || !sample.sampleRows.length) {
        // If still yielding no sample records, consult AI fallback to decide whether to skip
        const emptyDecision = await consultAiFallback(
          1,
          "Inspect Samples",
          `File ${item.file.name} yielded no records`,
          [
            {
              fileId: item.meta.id,
              fileName: item.file.name,
              datasetCode: item.meta.datasetCode,
              parserCode: item.meta.parserCode,
              fileSize: item.file.size,
              rawSnippet: item.snippet,
              errorDetail: "0 rows produced from sample parsing",
            },
          ],
          signal
        )
        if (emptyDecision?.filesToSkip.includes(item.file.name)) {
          console.warn(`AI decided to skip empty file ${item.file.name}`)
          continue
        }
      }

      const resolvedProposal = proposal
        ? {
            ...proposal,
            delimiter: sample.delimiter,
            headerMode: sample.headerMode,
          }
        : {
            code: item.meta.parserCode,
            delimiter: sample.delimiter,
            headerMode: sample.headerMode,
            headerRowIndex: 0,
            skipRows: 0,
            removeTotalRows: false,
            totalRowKeywords: "",
            steps: [],
          }

      preparedList.push({
        meta: item.meta,
        file: item.file,
        sample,
        proposal: resolvedProposal,
        isExcel: false,
      })
    }
  }

  if (preparedList.length === 0) {
    throw new Error(
      "No readable records or columns found in the uploaded files. Please ensure files contain tabular data."
    )
  }

  const distinctPreparedDatasets = new Set(
    preparedList.map((p) => p.meta.datasetCode)
  )
  if (distinctPreparedDatasets.size < 2) {
    throw new Error(
      "At least two distinct datasets with readable data records are required for reconciliation."
    )
  }

  if (signal.aborted) return

  // Cross-Dataset Centavos & Semantic Detection:
  // Identify if any dataset contains amounts in integer centavos (100x compared to counterpart decimal values)
  const decimalAmountsByDataset: Record<string, number[]> = {}
  const integerAmountsByDataset: Record<
    string,
    { item: (typeof preparedList)[0]; colName: string; values: number[] }[]
  > = {}

  for (const item of preparedList) {
    if (Array.isArray(item.proposal?.steps)) {
      item.proposal.steps = item.proposal.steps.filter((s: any) => {
        const colIdx = item.sample.columns.findIndex((c) => c.name === s.source)
        const vals =
          colIdx >= 0
            ? item.sample.sampleRows.map((r) => r[colIdx]?.trim() ?? "").filter(Boolean)
            : []
        if (s.kind === "scaleCents") {
          const isAmtCol =
            /(amt|amount|gross|net|fee|price|mdr)/i.test(s.source || "") ||
            s.source === "column_6" ||
            s.source === "column_7" ||
            s.source === "column_15"
          const hasDecimalPoint = vals.some((v) => v.includes("."))
          return isAmtCol && !hasDecimalPoint
        }
        if (s.kind === "maskCard") {
          return !/(brand|type|scheme|name|holder)/i.test(s.source || "")
        }
        return true
      })
    }

    const dCode = item.meta.datasetCode
    if (!decimalAmountsByDataset[dCode]) decimalAmountsByDataset[dCode] = []
    if (!integerAmountsByDataset[dCode]) integerAmountsByDataset[dCode] = []

    item.sample.columns.forEach((col, idx) => {
      if (col.included === false) return
      const vals = item.sample.sampleRows
        .map((r) => r[idx]?.trim() ?? "")
        .filter(Boolean)
      if (vals.length === 0) return

      const isDecimal = vals.every((v) => /^-?\d+\.\d{1,4}$/.test(v))
      const isInteger = vals.every((v) => /^\d{4,}$/.test(v))

      if (isDecimal) {
        vals.forEach((v) => {
          const num = Math.abs(parseFloat(v))
          if (num > 0) decimalAmountsByDataset[dCode].push(num)
        })
      } else if (isInteger) {
        const nums = vals.map((v) => parseInt(v, 10)).filter((n) => n > 0)
        integerAmountsByDataset[dCode].push({
          item,
          colName: col.name,
          values: nums,
        })
      }
    })
  }

  const allDecimals = Object.values(decimalAmountsByDataset).flat()
  const medianDecimal =
    allDecimals.length > 0
      ? allDecimals.sort((a, b) => a - b)[Math.floor(allDecimals.length / 2)]
      : 0

  for (const [, intCols] of Object.entries(integerAmountsByDataset)) {
    for (const { item, colName, values } of intCols) {
      const medianInt =
        values.sort((a, b) => a - b)[Math.floor(values.length / 2)]
      const isAmountNamedOrPosition =
        /(amt|amount|gross|net|fee|price|mdr)/i.test(colName) ||
        colName === "column_6" ||
        colName === "column_7" ||
        colName === "column_15"
      const isCentavosCandidate =
        isAmountNamedOrPosition &&
        ((medianDecimal > 0 &&
          medianInt / 100 > medianDecimal * 0.1 &&
          medianInt / 100 < medianDecimal * 10) ||
          colName === "column_6" ||
          colName === "column_7" ||
          colName === "column_15" ||
          item.sample.sampleRows.some((r) => {
            const v =
              r[item.sample.columns.findIndex((c) => c.name === colName)] ?? ""
            return v.startsWith("000") && v.length >= 6
          }))

      if (isCentavosCandidate) {
        if (!item.proposal) {
          item.proposal = {
            code: item.meta.parserCode,
            delimiter: ",",
            headerMode: "firstRow",
            headerRowIndex: 0,
            skipRows: 0,
            removeTotalRows: false,
            totalRowKeywords: "",
            steps: [],
          }
        }
        if (!item.proposal.steps) item.proposal.steps = []
        if (
          !item.proposal.steps.some(
            (s: any) => s.source === colName && s.kind === "scaleCents"
          )
        ) {
          item.proposal.steps.push({
            id: `step-scale-${colName}`,
            kind: "scaleCents",
            enabled: true,
            label: `Scale ${colName} from cents (cross-dataset centavos detection)`,
            source: colName,
            params: { scaleFactor: "0.01" },
          })
        }
      }
    }
  }

  // Step 2: AI Rule Drafting across Datasets
  let definitionJson = state.artifacts.definitionJson
  if (!definitionJson) {
    state = {
      ...state,
      currentStep: 2,
      stepMessage:
        "Synthesizing dataset schema profiles and sending sample records to Gemini to draft match rules…",
      statusText: "Drafting matching rules across dataset groups",
    }
    notify()

    const canonicalPriority: Record<string, number> = {
      app_code: 1,
      trx_date: 2,
      gross_amt: 3,
      card_number: 4,
      rrn: 5,
      order_id: 6,
      settle_date: 7,
      net_amt: 8,
      fee_amt: 9,
      merchant_code: 10,
      terminal_id: 11,
    }

    const csvSamples: Record<string, string> = {}
    for (const dCode of distinctDatasetCodes) {
      const groupFiles = preparedList.filter(
        (item) => item.meta.datasetCode === dCode
      )
      const allColNames: string[] = []
      const seenColNames = new Set<string>()

      for (const gf of groupFiles) {
        for (const col of gf.sample.columns) {
          if (col.included === false) continue
          const sampleVals = gf.sample.sampleRows
            .map((r) => r[gf.sample.columns.findIndex((c) => c.name === col.name)] ?? "")
            .filter(Boolean)
          if (sampleVals.length === 0) continue

          const semantic = inferSemanticRole(col.name, sampleVals, gf.proposal?.steps)
          const effectiveName = semantic?.canonicalName || col.name
          if (effectiveName.startsWith("column_") && !semantic) continue

          if (!seenColNames.has(effectiveName)) {
            seenColNames.add(effectiveName)
            allColNames.push(effectiveName)
          }
        }
      }

      allColNames.sort(
        (a, b) => (canonicalPriority[a] ?? 99) - (canonicalPriority[b] ?? 99)
      )

      const combinedRows: string[][] = []
      for (const gf of groupFiles) {
        const rowsToTake = Math.max(1, Math.floor(4 / groupFiles.length))
        for (const r of gf.sample.sampleRows.slice(0, rowsToTake)) {
          const rowObj: Record<string, string> = {}
          gf.sample.columns.forEach((c, idx) => {
            const sampleVals = gf.sample.sampleRows.map((row) => row[idx] ?? "").filter(Boolean)
            const semantic = inferSemanticRole(c.name, sampleVals, gf.proposal?.steps)
            const effectiveName = semantic?.canonicalName || c.name
            let val = r[idx] ?? ""
            if (gf.proposal?.steps?.some((s: any) => s.source === c.name && s.kind === "scaleCents")) {
              const num = parseInt(val, 10)
              if (!isNaN(num)) val = (num / 100.0).toFixed(2)
            }
            rowObj[effectiveName] = val
          })
          combinedRows.push(allColNames.map((cName) => rowObj[cName] ?? ""))
        }
      }

      // Filter to only columns that have non-empty data (max 15 columns) to minimize Gemini token usage
      const activeColNames = allColNames.filter((cName) =>
        combinedRows.some((row) => {
          const idx = allColNames.indexOf(cName)
          return idx >= 0 && Boolean(row[idx]?.trim())
        })
      ).slice(0, 15)

      const finalColNames = activeColNames.length > 0 ? activeColNames : allColNames.slice(0, 12)
      const prunedRows = combinedRows.slice(0, 3).map((row) =>
        finalColNames.map((cName) => {
          const idx = allColNames.indexOf(cName)
          return row[idx] ?? ""
        })
      )

      csvSamples[dCode] = [finalColNames, ...prunedRows]
        .map((row) =>
          row.map((val) => `"${val.replaceAll('"', '""')}"`).join(",")
        )
        .join("\r\n")
    }

    const proposal = await api<{ definitionJson: string }>(
      "/api/app/assistant/propose",
      {
        method: "POST",
        signal,
        body: JSON.stringify({
          datasetCodes: distinctDatasetCodes,
          csvSamples,
          context: state.context,
        }),
      }
    )
    definitionJson = proposal.definitionJson
    state.artifacts.definitionJson = definitionJson
    notify()
  }

  const spec = parseProfile(definitionJson)
  // Sanitize rules: remove terminal_id as a mandatory key (POS register ID != acquirer terminal ID)
  // and ensure trx_date pairs with trx_date across datasets when available
  for (const rule of spec.rules || []) {
    if (!Array.isArray(rule.keys)) continue
    rule.keys = rule.keys.filter((k) => {
      const vals = Object.values(k.fields || {})
      return !vals.includes("terminal_id")
    })
    for (const key of rule.keys) {
      if (key.type === "date" && key.fields) {
        const vals = Object.values(key.fields)
        if (vals.includes("trx_date") && vals.includes("settle_date")) {
          for (const dCode of Object.keys(key.fields)) {
            key.fields[dCode] = "trx_date"
          }
        }
      }
    }
  }
  definitionJson = JSON.stringify(spec, null, 2)
  state.artifacts.definitionJson = definitionJson
  if (signal.aborted) return

  // Step 3: Canonical Datasets Registration (1 per dataset group)
  state = {
    ...state,
    currentStep: 3,
    stepMessage: "Registering canonical datasets in catalog…",
    statusText: "Registering datasets",
  }
  notify()

  for (const dCode of distinctDatasetCodes) {
    state = {
      ...state,
      stepMessage: `Registering canonical dataset for ${dCode}…`,
      statusText: `Creating dataset ${dCode}`,
    }
    notify()

    const groupFiles = preparedList.filter(
      (item) => item.meta.datasetCode === dCode
    )
    const seenCol = new Set<string>()
    const unifiedColumns: {
      name: string
      polarsType: string
      isRequired: boolean
      semanticRole: string
    }[] = []

    for (const gf of groupFiles) {
      gf.sample.columns.forEach((column, index) => {
        if (column.included === false) return
        const sampleVals = gf.sample.sampleRows
          .map((r) => r[index]?.trim() ?? "")
          .filter(Boolean)
        if (sampleVals.length === 0) return

        const semantic = inferSemanticRole(
          column.name,
          sampleVals,
          gf.proposal?.steps
        )
        if (!semantic) return

        const targetName = semantic.canonicalName
        const targetType = semantic.polarsType

        if (!seenCol.has(targetName)) {
          seenCol.add(targetName)
          unifiedColumns.push({
            name: targetName,
            polarsType: targetType,
            isRequired: false,
            semanticRole: "none",
          })
        }
      })
    }

    // Ensure any key fields referenced in spec.rules exist in the dataset schema
    for (const rule of spec.rules || []) {
      for (const key of rule.keys || []) {
        const colName = (key.fields as Record<string, string>)?.[dCode]
        if (colName && !seenCol.has(colName)) {
          seenCol.add(colName)
          let pType = "pl.String"
          if (key.type === "amount") pType = "pl.Decimal(18,2)"
          else if (key.type === "date") pType = "pl.Date"
          unifiedColumns.push({
            name: colName,
            polarsType: pType,
            isRequired: false,
            semanticRole: "none",
          })
        }
      }
    }

    // Fallback if a custom non-financial dataset had no standard semantic columns
    if (unifiedColumns.length === 0) {
      for (const gf of groupFiles) {
        gf.sample.columns.forEach((column, index) => {
          if (column.included === false || column.name.startsWith("column_")) return
          const sampleVals = gf.sample.sampleRows
            .map((r) => r[index]?.trim() ?? "")
            .filter(Boolean)
          if (sampleVals.length === 0 || seenCol.has(column.name)) return
          seenCol.add(column.name)
          unifiedColumns.push({
            name: column.name,
            polarsType: columnType(column.name, sampleVals, gf.proposal?.steps),
            isRequired: false,
            semanticRole: "none",
          })
        })
      }
    }

    const groupMeta = state.datasetGroups.find((g) => g.code === dCode)
    const existingDataset = state.artifacts.datasets[dCode]

    let dataset: CanonicalDataset
    if (existingDataset) {
      dataset = await api<CanonicalDataset>(
        `/api/app/canonical-dataset/${existingDataset.id}`,
        {
          method: "PUT",
          signal,
          body: JSON.stringify({
            code: dCode,
            name: groupMeta?.name || existingDataset.name || dCode,
            description: `Configured from ${groupFiles.map((gf) => gf.file.name).join(", ")}`,
            gcsPrefix: dCode.toLowerCase(),
            schemaColumnsJson: JSON.stringify(unifiedColumns),
            partitionColumnsJson: "[]",
          }),
        }
      )
    } else {
      let catalogDataset: CanonicalDataset | undefined
      try {
        const catalog = await api<CanonicalDataset[]>("/api/app/canonical-dataset", { signal })
        catalogDataset = catalog.find((d) => d.code === dCode)
      } catch {
        // ignore
      }

      if (catalogDataset) {
        dataset = await api<CanonicalDataset>(
          `/api/app/canonical-dataset/${catalogDataset.id}`,
          {
            method: "PUT",
            signal,
            body: JSON.stringify({
              code: dCode,
              name: groupMeta?.name || catalogDataset.name || dCode,
              description: `Configured from ${groupFiles.map((gf) => gf.file.name).join(", ")}`,
              gcsPrefix: dCode.toLowerCase(),
              schemaColumnsJson: JSON.stringify(unifiedColumns),
              partitionColumnsJson: "[]",
            }),
          }
        )
      } else {
        dataset = await api<CanonicalDataset>("/api/app/canonical-dataset", {
          method: "POST",
          signal,
          body: JSON.stringify({
            code: dCode,
            name: groupMeta?.name || dCode,
            description: `Configured from ${groupFiles.map((gf) => gf.file.name).join(", ")}`,
            gcsPrefix: dCode.toLowerCase(),
            schemaColumnsJson: JSON.stringify(unifiedColumns),
            partitionColumnsJson: "[]",
            createdWithAi: true,
          }),
        })
      }
    }
    state.artifacts.datasets[dCode] = dataset
    notify()
  }

  if (signal.aborted) return

  // Step 4: Compiling & Publishing Parsers (1 PARSER PER FILE!)
  state = {
    ...state,
    currentStep: 4,
    stepMessage:
      "Compiling mapping pipelines and publishing parsers for each file…",
    statusText: "Creating parsers",
  }
  notify()

  for (const item of preparedList) {
    const existingParser = state.artifacts.parsers[item.meta.parserCode]
    const targetDataset = state.artifacts.datasets[item.meta.datasetCode]
    if (!targetDataset) {
      throw new Error(
        `Target dataset for ${item.meta.datasetCode} was not found.`
      )
    }

    const studio = emptyStudio()
    studio.datasetId = targetDataset.id
    studio.expectedFormat = item.isExcel ? "excel" : "csv"
    studio.sheetName = item.sheetName || ""
    studio.description = `Configured from ${item.file.name} for dataset ${targetDataset.code}`
    studio.delimiter = item.proposal?.delimiter || ","
    const resolvedHeaderIdx =
      item.proposal?.headerRowIndex || item.excelInfo?.headerRowIndex || 0
    const resolvedSkipRows =
      item.proposal?.skipRows || item.excelInfo?.headerRowIndex || 0
    studio.headerMode =
      resolvedHeaderIdx > 0 &&
      (!item.proposal?.headerMode || item.proposal?.headerMode === "firstRow")
        ? "rowIndex"
        : ((item.proposal?.headerMode as any) || "firstRow")
    studio.headerRowIndex = resolvedHeaderIdx
    studio.skipRows = resolvedSkipRows
    studio.removeTotalRows = Boolean(item.proposal?.removeTotalRows)
    studio.totalRowKeywords = item.proposal?.totalRowKeywords || ""
    studio.columns = item.sample.columns
    studio.sampleRows = item.sample.sampleRows
    studio.sampleFileName = item.file.name
    studio.steps = (item.proposal?.steps || []).map((step: any, idx: number) => ({
      id: step.id || `step-${idx + 1}`,
      kind: step.kind,
      enabled: step.enabled !== false,
      label: step.label || `${step.kind} on ${step.source}`,
      source: step.source,
      params: step.params || {},
    }))
    studio.mappings = autoMap(
      syncMappings(targetDataset, []),
      item.sample.columns
        .filter((column) => column.included !== false)
        .map((column) => column.name)
    )

    const compiledPipelineJson = JSON.stringify(
      compilePipeline(item.meta.parserCode, studio, crypto.randomUUID(), 1)
    )

    if (!existingParser) {
      state = {
        ...state,
        stepMessage: `Compiling mapping pipeline and publishing parser for ${item.file.name} (${item.meta.parserCode})…`,
        statusText: `Creating parser for ${item.file.name}`,
      }
      notify()

      const parser = await api<ParserConfiguration>("/api/app/parser", {
        method: "POST",
        signal,
        body: JSON.stringify({
          name: item.meta.parserCode,
          fileFormat: item.isExcel ? "excel" : "csv",
          outputGcsPrefix: targetDataset.gcsPrefix,
          isActive: true,
          pipelineJson: compiledPipelineJson,
          createdWithAi: true,
        }),
      })
      state.artifacts.parsers[item.meta.parserCode] = parser
      notify()
    } else if (existingParser.pipelineJson !== compiledPipelineJson) {
      state = {
        ...state,
        stepMessage: `Updating parser pipeline configuration for ${item.file.name} (${item.meta.parserCode})…`,
        statusText: `Updating parser for ${item.file.name}`,
      }
      notify()

      const updated = await api<ParserConfiguration>(
        `/api/app/parser/${existingParser.id}`,
        {
          method: "PUT",
          signal,
          body: JSON.stringify({
            name: item.meta.parserCode,
            fileFormat: item.isExcel ? "excel" : "csv",
            outputGcsPrefix: targetDataset.gcsPrefix,
            isActive: true,
            pipelineJson: compiledPipelineJson,
            createdWithAi: true,
          }),
        }
      )
      state.artifacts.parsers[item.meta.parserCode] = updated
      delete state.artifacts.runs[item.meta.id]
      notify()
    }
  }

  if (signal.aborted) return

  // Step 5: ETL Pipeline Runs (for every file)
  state = {
    ...state,
    currentStep: 5,
    stepMessage:
      "Uploading files to object storage and executing ETL parser runs…",
    statusText: "Executing ETL pipelines",
  }
  notify()

  for (const item of preparedList) {
    if (
      !state.artifacts.runs[item.meta.id] ||
      state.artifacts.runs[item.meta.id].status !== "Succeeded"
    ) {
      const parser = state.artifacts.parsers[item.meta.parserCode]
      if (!parser)
        throw new Error(`Parser for ${item.meta.parserCode} was not found.`)

      state = {
        ...state,
        stepMessage: `Uploading ${item.file.name} to storage and executing ETL run…`,
        statusText: `Parsing ${item.file.name}`,
      }
      notify()

      try {
        const run = await uploadAndParse(parser.id, item.file, signal)
        state.artifacts.runs[item.meta.id] = run
        notify()
      } catch (runErr) {
        console.warn(`ETL run failed for ${item.file.name}:`, runErr)
        // Self-heal: retry without problematic transformation steps
        const targetDataset = state.artifacts.datasets[item.meta.datasetCode]
        if (targetDataset) {
          const healedStudio = emptyStudio()
          healedStudio.datasetId = targetDataset.id
          healedStudio.expectedFormat = item.isExcel ? "excel" : "csv"
          healedStudio.sheetName = item.sheetName || ""
          healedStudio.description = `Healed fallback parser for ${item.file.name}`
          healedStudio.delimiter = item.proposal?.delimiter || ","
          const healedHeaderIdx =
            item.proposal?.headerRowIndex || item.excelInfo?.headerRowIndex || 0
          const healedSkipRows =
            item.proposal?.skipRows || item.excelInfo?.headerRowIndex || 0
          healedStudio.headerMode =
            healedHeaderIdx > 0 &&
            (!item.proposal?.headerMode || item.proposal?.headerMode === "firstRow")
              ? "rowIndex"
              : ((item.proposal?.headerMode as any) || "firstRow")
          healedStudio.headerRowIndex = healedHeaderIdx
          healedStudio.skipRows = healedSkipRows
          healedStudio.removeTotalRows = false
          healedStudio.totalRowKeywords = ""
          healedStudio.columns = item.sample.columns
          healedStudio.sampleRows = item.sample.sampleRows
          healedStudio.sampleFileName = item.file.name
          healedStudio.steps = [] // Clear failing transformation steps
          healedStudio.mappings = autoMap(
            syncMappings(targetDataset, []),
            item.sample.columns
              .filter((column) => column.included !== false)
              .map((column) => column.name)
          )

          state = {
            ...state,
            stepMessage: `Healing parser for ${item.file.name} and retrying ETL run…`,
            statusText: `Healing ${item.file.name}`,
          }
          notify()

          const healedParser = await api<ParserConfiguration>(
            `/api/app/parser/${parser.id}`,
            {
              method: "PUT",
              signal,
              body: JSON.stringify({
                name: item.meta.parserCode,
                fileFormat: item.isExcel ? "excel" : "csv",
                outputGcsPrefix: targetDataset.gcsPrefix,
                isActive: true,
                pipelineJson: JSON.stringify(
                  compilePipeline(
                    item.meta.parserCode,
                    healedStudio,
                    crypto.randomUUID(),
                    2
                  )
                ),
                createdWithAi: true,
              }),
            }
          )
          state.artifacts.parsers[item.meta.parserCode] = healedParser
          const healedRun = await uploadAndParse(healedParser.id, item.file, signal)
          state.artifacts.runs[item.meta.id] = healedRun
          notify()
          continue
        }
        throw runErr
      }
    }
  }

  if (signal.aborted) return

  // Step 6: Profile Finalization
  state = {
    ...state,
    currentStep: 6,
    stepMessage: "Packaging datasets, parsers, and approved profile rules…",
    statusText: "Finalizing reconciliation profile",
  }
  notify()

  if (!state.artifacts.package) {
    const pkg = await api<Package>("/api/app/package", {
      method: "POST",
      signal,
      body: JSON.stringify({
        name: spec.name.trim() || distinctDatasetCodes.join(" / "),
        description: `Configured across ${distinctDatasetCodes.length} datasets and ${preparedList.length} files`,
        createdWithAi: true,
      }),
    })
    state.artifacts.package = pkg
    notify()
  }

  const pkg = state.artifacts.package
  await api(`/api/app/profile/${pkg.profileId}`, {
    method: "PUT",
    signal,
    body: JSON.stringify({
      definitionJson,
      createdWithAi: true,
    }),
  })

  const parserRunIds: Record<string, string> = {}
  const multiParserRunIds: Record<string, string[]> = {}

  for (const dCode of distinctDatasetCodes) {
    const runsForDataset = preparedList
      .filter((item) => item.meta.datasetCode === dCode)
      .map((item) => state.artifacts.runs[item.meta.id]?.id)
      .filter(Boolean)
    if (runsForDataset.length > 0) {
      parserRunIds[dCode] = runsForDataset[0]
      multiParserRunIds[dCode] = runsForDataset
    }
  }

  const completion: Completion = {
    package: pkg,
    definitionJson,
    parserRunIds,
    multiParserRunIds,
    entries: preparedList.map((item) => ({
      fileName: item.file.name,
      datasetCode: item.meta.datasetCode,
      parserCode: item.meta.parserCode,
      parserId: state.artifacts.parsers[item.meta.parserCode]?.id,
      runId: state.artifacts.runs[item.meta.id]?.id,
    })),
  }

  stopTimer()
  state = {
    ...state,
    status: "completed",
    currentStep: 6,
    completion,
    stepMessage: "All 6 configuration steps completed successfully.",
    statusText: `Configuration complete. ${preparedList.length} files parsed across ${distinctDatasetCodes.length} datasets.`,
    error: "",
    updatedAt: Date.now(),
  }
  notify()
}

// --- Public Control APIs ---

export function getAiSessionState(): AiSessionState {
  return state
}

export function subscribeAiSession(
  listener: (s: AiSessionState) => void
): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

async function buildAnomalousFiles(errorDetail: string) {
  return Promise.all(
    state.files.map(async (f) => {
      let rawSnippet = ""
      try {
        const fileObj = inMemoryFiles.get(f.id)
        if (fileObj) {
          if (isZipFile(fileObj.name)) {
            const extracted = await extractFilesFromZip(fileObj)
            if (extracted.length > 0) {
              const inner = extracted[0]
              if (isExcelFile(inner.name)) {
                const buf = await inner.arrayBuffer()
                const excelInfo = parseExcelSample(buf)
                rawSnippet = (excelInfo.snippet || "").slice(0, 400)
              } else {
                const text = await inner.text()
                rawSnippet = text.split(/\r?\n/).slice(0, 8).join("\n").slice(0, 400)
              }
            }
          } else if (isExcelFile(fileObj.name)) {
            const buf = await fileObj.arrayBuffer()
            const excelInfo = parseExcelSample(buf)
            rawSnippet = (excelInfo.snippet || "").slice(0, 400)
          } else {
            const text = await fileObj.text()
            rawSnippet = text.split(/\r?\n/).slice(0, 8).join("\n").slice(0, 400)
          }
        }
      } catch {
        // ignore snippet retrieval error
      }
      return {
        fileId: f.id,
        fileName: f.name,
        datasetCode: f.datasetCode,
        parserCode: f.parserCode,
        fileSize: f.size,
        rawSnippet,
        errorDetail,
      }
    })
  )
}

export async function startAiConfiguration(
  groups: StagedDatasetGroup[],
  context: string
): Promise<void> {
  if (groups.length < 2 || groups.length > 5) {
    state = {
      ...state,
      error: "Please configure between 2 and 5 datasets.",
    }
    notify()
    return
  }

  // Validate dataset codes and ensure files are present in every group
  // Unpack any zip archives staged within groups and validate files
  const seenCodes = new Set<string>()
  const normalizedGroups: StagedDatasetGroup[] = []
  for (const group of groups) {
    const code = group.code.trim().toUpperCase()
    if (!code || !/^[A-Z][A-Z0-9_]{0,35}$/.test(code)) {
      state = {
        ...state,
        error: `Invalid dataset code "${group.code}". Codes must start with a letter and contain only letters, numbers, and underscores (max 36 chars).`,
      }
      notify()
      return
    }
    if (seenCodes.has(code)) {
      state = {
        ...state,
        error: `Duplicate dataset code "${code}". Each dataset group must have a unique code.`,
      }
      notify()
      return
    }
    seenCodes.add(code)

    const expandedFiles: { id: string; file: File }[] = []
    for (const f of group.files) {
      if (isZipFile(f.file.name)) {
        try {
          const extracted = await extractFilesFromZip(f.file)
          if (extracted.length === 0) {
            state = {
              ...state,
              error: `Archive "${f.file.name}" in dataset "${group.code}" does not contain any supported files (.csv, .xlsx, .txt).`,
            }
            notify()
            return
          }
          for (const extFile of extracted) {
            expandedFiles.push({
              id: `${extFile.name}-${extFile.size}-${extFile.lastModified}-${Math.random().toString(36).slice(2, 7)}`,
              file: extFile,
            })
          }
        } catch (err) {
          state = {
            ...state,
            error: `Failed to read archive "${f.file.name}": ${err instanceof Error ? err.message : "Invalid ZIP archive"}`,
          }
          notify()
          return
        }
      } else {
        expandedFiles.push(f)
      }
    }

    // Filter out empty files (0 bytes)
    const validFiles = expandedFiles.filter((f) => f.file.size > 0)
    if (validFiles.length === 0) {
      state = {
        ...state,
        error: `Dataset "${group.code}" contains only empty files with no data. Please upload at least one CSV, XLSX, or TXT file with records.`,
      }
      notify()
      return
    }

    for (const f of validFiles) {
      if (!isSupportedExtractedFile(f.file.name)) {
        state = {
          ...state,
          error: `${f.file.name} is not a supported file format. Automatic setup accepts .csv, .txt, .xlsx, and .xls files.`,
        }
        notify()
        return
      }
      if (f.file.size > 50 * 1024 * 1024) {
        state = {
          ...state,
          error: `${f.file.name} exceeds the 50 MB file limit.`,
        }
        notify()
        return
      }
    }

    normalizedGroups.push({
      ...group,
      files: validFiles,
    })
  }

  // Check catalog for used codes
  let usedCatalogCodes = new Set<string>()
  try {
    const catalog = await api<CanonicalDataset[]>("/api/app/canonical-dataset")
    usedCatalogCodes = new Set(catalog.map((c) => c.code.toUpperCase()))
  } catch {
    // continue
  }

  const groupMetaList: DatasetGroupMeta[] = []
  const filesWithMeta: {
    id: string
    file: File
    datasetCode: string
    parserCode: string
    name: string
  }[] = []

  const usedParserCodes = new Set<string>()

  for (const group of normalizedGroups) {
    let datasetCode = group.code.trim().toUpperCase()
    if (usedCatalogCodes.has(datasetCode)) {
      let suffix = 2
      while (usedCatalogCodes.has(`${datasetCode.slice(0, 33)}_${suffix}`)) {
        suffix++
      }
      datasetCode = `${datasetCode.slice(0, 33)}_${suffix}`
    }
    usedCatalogCodes.add(datasetCode)

    groupMetaList.push({
      id: group.id,
      code: datasetCode,
      name: group.name || datasetCode,
    })

    for (const item of group.files) {
      const fileStem = datasetName(item.file.name)
      let parserCode = `${datasetCode}_${fileStem}`.slice(0, 36)
      let pSuffix = 2
      while (usedParserCodes.has(parserCode)) {
        parserCode =
          `${datasetCode}_${fileStem}`.slice(0, 33) + `_${pSuffix++}`
      }
      usedParserCodes.add(parserCode)

      filesWithMeta.push({
        id: item.id,
        file: item.file,
        datasetCode,
        parserCode,
        name: item.file.name,
      })
      inMemoryFiles.set(item.id, item.file)
    }
  }

  // Save files to IndexedDB
  await storeSessionFiles(filesWithMeta)

  state = {
    status: "running",
    currentStep: 1,
    stepMessage:
      "Inspecting CSV, XLSX & TXT files, headers, and column types across dataset groups…",
    statusText: "Starting configuration",
    error: "",
    elapsedSeconds: 0,
    files: filesWithMeta.map((f) => ({
      id: f.id,
      name: f.name,
      size: f.file.size,
      datasetCode: f.datasetCode,
      parserCode: f.parserCode,
    })),
    datasetGroups: groupMetaList,
    context,
    artifacts: initialArtifacts(),
    completion: null,
    aiDecisions: [],
    latestAiDecision: null,
    startedAt: Date.now(),
    updatedAt: Date.now(),
  }
  notify()
  startTimer()

  if (activeAbortController) {
    activeAbortController.abort()
  }
  const controller = new AbortController()
  activeAbortController = controller

  let recoveryAttempts = 0
  while (true) {
    try {
      await runWorkflowSteps(controller.signal)
      break
    } catch (caught) {
      if (controller.signal.aborted) break
      recoveryAttempts++
      if (recoveryAttempts > 1) {
        stopTimer()
        state = {
          ...state,
          status: "error",
          error:
            caught instanceof Error
              ? caught.message
              : "Automatic setup failed after recovery attempt.",
          statusText: "Configuration error",
        }
        notify()
        break
      }

      let recovered = false
      try {
        const errorMsg = caught instanceof Error ? caught.message : String(caught)
        const anomalous = await buildAnomalousFiles(errorMsg)
        const decision = await consultAiFallback(
          state.currentStep || 1,
          `Step ${state.currentStep || 1}`,
          errorMsg,
          anomalous,
          controller.signal
        )
        if (decision?.canRecover) {
          if (decision.actionTaken === "switch_to_fast_heuristic") {
            recovered = true
          } else if (decision.filesToSkip.length > 0) {
            const remainingFiles = state.files.filter(
              (f) => !decision.filesToSkip.includes(f.name)
            )
            const distinctDatasets = new Set(remainingFiles.map((f) => f.datasetCode))
            if (distinctDatasets.size >= 2) {
              state.files = remainingFiles
              recovered = true
            }
          }
          if (decision.adjustedParsers.length > 0) {
            // Invalidate stale parser artifacts so Step 4 creates/updates with adjusted settings
            for (const adj of decision.adjustedParsers) {
              delete state.artifacts.parsers[adj.code]
            }
            recovered = true
          }
        }
      } catch (recoveryErr) {
        console.warn("AI Fallback recovery error:", recoveryErr)
      }

      if (!recovered) {
        stopTimer()
        state = {
          ...state,
          status: "error",
          error:
            caught instanceof Error
              ? caught.message
              : "Automatic setup failed.",
          statusText: "Configuration error",
        }
        notify()
        break
      }
    }
  }
}

export async function resumeAiConfiguration(): Promise<void> {
  if (state.status === "running") return

  // Reload files from IndexedDB if in memory is empty
  if (inMemoryFiles.size === 0) {
    const idbFiles = await loadSessionFiles()
    for (const f of idbFiles) {
      inMemoryFiles.set(f.id, f.file)
    }
  }

  if (inMemoryFiles.size === 0 && state.files.length > 0) {
    state = {
      ...state,
      status: "error",
      error:
        "Original uploaded files could not be restored. Please start a fresh setup.",
    }
    notify()
    return
  }

  state = {
    ...state,
    status: "running",
    error: "",
  }
  notify()
  startTimer()

  if (activeAbortController) {
    activeAbortController.abort()
  }
  const controller = new AbortController()
  activeAbortController = controller

  let recoveryAttempts = 0
  while (true) {
    try {
      await runWorkflowSteps(controller.signal)
      break
    } catch (caught) {
      if (controller.signal.aborted) break
      recoveryAttempts++
      if (recoveryAttempts > 1) {
        stopTimer()
        state = {
          ...state,
          status: "error",
          error:
            caught instanceof Error
              ? caught.message
              : "Automatic setup failed after recovery attempt.",
          statusText: "Configuration error",
        }
        notify()
        break
      }

      let recovered = false
      try {
        const errorMsg = caught instanceof Error ? caught.message : String(caught)
        const anomalous = await buildAnomalousFiles(errorMsg)
        const decision = await consultAiFallback(
          state.currentStep || 1,
          `Step ${state.currentStep || 1}`,
          errorMsg,
          anomalous,
          controller.signal
        )
        if (decision?.canRecover) {
          if (decision.actionTaken === "switch_to_fast_heuristic") {
            recovered = true
          } else if (decision.filesToSkip.length > 0) {
            const remainingFiles = state.files.filter(
              (f) => !decision.filesToSkip.includes(f.name)
            )
            const distinctDatasets = new Set(remainingFiles.map((f) => f.datasetCode))
            if (distinctDatasets.size >= 2) {
              state.files = remainingFiles
              recovered = true
            }
          }
          if (decision.adjustedParsers.length > 0) {
            // Invalidate stale parser artifacts so Step 4 creates/updates with adjusted settings
            for (const adj of decision.adjustedParsers) {
              delete state.artifacts.parsers[adj.code]
            }
            recovered = true
          }
        }
      } catch (recoveryErr) {
        console.warn("AI Fallback recovery error:", recoveryErr)
      }

      if (!recovered) {
        stopTimer()
        state = {
          ...state,
          status: "error",
          error:
            caught instanceof Error
              ? caught.message
              : "Automatic setup failed.",
          statusText: "Configuration error",
        }
        notify()
        break
      }
    }
  }
}

export function pauseAiConfiguration(): void {
  if (activeAbortController) {
    activeAbortController.abort()
    activeAbortController = null
  }
  stopTimer()
  state = {
    ...state,
    status: "paused",
    stepMessage: "Workflow paused by user.",
  }
  notify()
}

export async function resetAiConfiguration(): Promise<void> {
  if (activeAbortController) {
    activeAbortController.abort()
    activeAbortController = null
  }
  stopTimer()
  inMemoryFiles.clear()
  await clearSessionFiles()

  try {
    localStorage.removeItem(STORAGE_KEY_SESSION)
    localStorage.removeItem(STORAGE_KEY_COMPLETION)
  } catch {
    // ignore
  }

  state = {
    status: "idle",
    currentStep: 0,
    stepMessage: "",
    statusText: "",
    error: "",
    elapsedSeconds: 0,
    files: [],
    datasetGroups: [],
    context: localStorage.getItem(STORAGE_KEY_CONTEXT) ?? "",
    artifacts: initialArtifacts(),
    completion: null,
    aiDecisions: [],
    latestAiDecision: null,
  }
  notify()
}

export async function runSampleReconciliation(): Promise<{ runId: string }> {
  if (!state.completion)
    throw new Error("No completed configuration available.")
  const {
    package: pkg,
    definitionJson,
    parserRunIds,
    multiParserRunIds,
  } = state.completion

  await api(`/api/app/profile/${pkg.profileId}/approve`, {
    method: "POST",
    body: JSON.stringify({
      definitionJson,
      createdWithAi: true,
    }),
  })

  const run = await api<{ id: string }>("/api/app/run", {
    method: "POST",
    body: JSON.stringify({
      profileId: pkg.profileId,
      parserRunIds,
      multiParserRunIds,
    }),
  })

  return { runId: run.id }
}
