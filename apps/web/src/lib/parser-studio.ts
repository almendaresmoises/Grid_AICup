import JSZip from "jszip"
import * as XLSX from "xlsx"
import {
  parseColumns,
  parsePartitions,
  type CanonicalDataset,
} from "@/lib/canonical"

export const parserNamePattern = /^[A-Za-z0-9_]+$/

export function isZipFile(name: string): boolean {
  return name.toLowerCase().endsWith(".zip")
}

export function isSupportedExtractedFile(name: string): boolean {
  const lower = name.toLowerCase()
  return (
    lower.endsWith(".csv") ||
    lower.endsWith(".txt") ||
    lower.endsWith(".xlsx") ||
    lower.endsWith(".xls")
  )
}

export async function extractFilesFromZip(zipFile: File): Promise<File[]> {
  const zip = new JSZip()
  const content = await zip.loadAsync(zipFile)
  const extractedFiles: File[] = []

  const entries: JSZip.JSZipObject[] = []
  content.forEach((relativePath, fileEntry) => {
    if (fileEntry.dir) return
    const basename = relativePath.split("/").pop() || relativePath
    if (basename.startsWith(".") || relativePath.startsWith("__MACOSX/")) return
    if (isSupportedExtractedFile(basename)) {
      entries.push(fileEntry)
    }
  })

  entries.sort((a, b) =>
    a.name.localeCompare(b.name, undefined, {
      numeric: true,
      sensitivity: "base",
    })
  )

  for (const entry of entries) {
    const blob = await entry.async("blob")
    // Skip empty files with no data
    if (blob.size === 0) continue

    const fileName = entry.name.split("/").pop() || entry.name
    let mimeType = "application/octet-stream"
    const lower = fileName.toLowerCase()
    if (lower.endsWith(".csv")) mimeType = "text/csv"
    else if (lower.endsWith(".txt")) mimeType = "text/plain"
    else if (lower.endsWith(".xlsx"))
      mimeType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    else if (lower.endsWith(".xls")) mimeType = "application/vnd.ms-excel"

    const file = new File([blob], fileName, {
      type: mimeType,
      lastModified: entry.date ? entry.date.getTime() : Date.now(),
    })
    extractedFiles.push(file)
  }

  return extractedFiles
}

export function detectDelimiter(text: string): string {
  const lines = text
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter(Boolean)
    .slice(0, 15)
  if (lines.length === 0) return ","
  const delims = [",", "\t", "|", ";"]
  let best = ","
  let bestScore = -1

  for (const d of delims) {
    const counts = lines.map((l) => l.split(d).length - 1)
    if (counts.every((c) => c > 0) && new Set(counts).size <= 2) {
      const score = counts.reduce((acc, c) => acc + c, 0)
      if (score > bestScore) {
        bestScore = score
        best = d
      }
    }
  }

  if (bestScore === -1) {
    for (const d of delims) {
      const score = lines.reduce((acc, l) => acc + (l.split(d).length - 1), 0)
      if (score > bestScore) {
        bestScore = score
        best = d
      }
    }
  }

  return best
}

export function detectHeaderMode(
  lines: string[],
  delimiter: string
): { headerMode: HeaderMode; headerRowIndex: number } {
  if (lines.length === 0) return { headerMode: "none", headerRowIndex: 0 }

  for (let i = 0; i < Math.min(lines.length, 5); i++) {
    const line = lines[i]
    if (
      line.startsWith("===") ||
      line.startsWith("---") ||
      line.startsWith("###") ||
      line.startsWith("***")
    ) {
      continue
    }

    const tokens = line.split(delimiter).map((t) => t.trim())
    if (tokens.length < 2) continue

    // If more than 40% of tokens are numbers, amounts, or 8-digit dates, this is a DATA row, not a header!
    const numericCount = tokens.filter(
      (t) =>
        /^-?\d+(\.\d+)?$/.test(t) ||
        /^\d{8}$/.test(t) ||
        /^\d{4}[-/]\d{2}[-/]\d{2}$/.test(t)
    ).length
    if (numericCount >= tokens.length * 0.4) {
      return { headerMode: "none", headerRowIndex: 0 }
    }

    // Check how many tokens contain letters
    const alphaCount = tokens.filter(
      (t) => /[A-Za-z]/.test(t) && !/^-?\d+(\.\d+)?$/.test(t)
    ).length
    if (alphaCount / tokens.length >= 0.4) {
      return {
        headerMode: i === 0 ? "firstRow" : "rowIndex",
        headerRowIndex: i,
      }
    }
  }

  return { headerMode: "none", headerRowIndex: 0 }
}

export type HeaderMode = "none" | "firstRow" | "rowIndex"
export type SourceFormat = "csv" | "excel" | "fixedWidth"
export type StepKind =
  | "parseDate"
  | "maskCard"
  | "scaleCents"
  | "pad"
  | "trimChars"
  | "extract"
  | "replace"
  | "trim"
  | "case"
  | "filterPrefix"

export type StudioStep = {
  id: string
  kind: StepKind
  enabled: boolean
  label: string
  source: string
  params: Record<string, string>
}

export type ColumnMapping = {
  target: string
  targetType: string
  required: boolean
  sourceKind: "unmapped" | "column" | "constant"
  sourceColumn: string
  constant: string
}

export type SourceColumn = {
  name: string
  included: boolean
  start?: number
  length?: number
}

export type StudioDocument = {
  proposalSource?: string
  proposalWarnings?: string[]
  description: string
  datasetId: string
  expectedFormat: SourceFormat
  delimiter: string
  headerMode: HeaderMode
  headerRowIndex: number
  skipRows: number
  sheetName: string
  removeTotalRows: boolean
  totalRowKeywords: string
  columns: SourceColumn[]
  sampleRows: string[][]
  sampleFileName: string
  mappings: ColumnMapping[]
  steps: StudioStep[]
}

export const stepCatalog: {
  kind: StepKind
  label: string
  group: "Transformations" | "Filters"
}[] = [
  { kind: "parseDate", label: "Parse date column", group: "Transformations" },
  { kind: "maskCard", label: "Mask card number", group: "Transformations" },
  {
    kind: "scaleCents",
    label: "Scale currency (cents)",
    group: "Transformations",
  },
  { kind: "pad", label: "Pad to fixed length", group: "Transformations" },
  {
    kind: "trimChars",
    label: "Remove leading zeros",
    group: "Transformations",
  },
  { kind: "extract", label: "Extract text", group: "Transformations" },
  { kind: "replace", label: "Replace text", group: "Transformations" },
  { kind: "trim", label: "Trim whitespace", group: "Transformations" },
  {
    kind: "case",
    label: "Conditional case derivation",
    group: "Transformations",
  },
  { kind: "filterPrefix", label: "Filter row prefixes", group: "Filters" },
]

export function emptyStudio(): StudioDocument {
  return {
    description: "",
    datasetId: "",
    expectedFormat: "csv",
    delimiter: ",",
    headerMode: "firstRow",
    headerRowIndex: 0,
    skipRows: 0,
    sheetName: "",
    removeTotalRows: false,
    totalRowKeywords: "TOTAL, GRAND TOTAL, SUB-TOTAL, TRAILER, SUMMARY",
    columns: [],
    sampleRows: [],
    sampleFileName: "",
    mappings: [],
    steps: [],
  }
}

export function newStep(kind: StepKind, source: string): StudioStep {
  const label = stepCatalog.find((item) => item.kind === kind)?.label ?? kind
  const params: Record<string, string> = {}
  if (kind === "parseDate") params.format = "%Y-%m-%d"
  if (kind === "pad") {
    params.length = "7"
    params.padChar = "0"
    params.side = "left"
  }
  if (kind === "trimChars") {
    params.characters = "0"
    params.side = "start"
  }
  if (kind === "extract") {
    params.offset = "0"
    params.length = "1"
  }
  if (kind === "replace") {
    params.pattern = ""
    params.replacement = ""
  }
  if (kind === "case") params.rules = ""
  if (kind === "filterPrefix") params.keywords = "HSH, TRL, TOTAL"
  return { id: crypto.randomUUID(), kind, enabled: true, label, source, params }
}

export function syncMappings(
  dataset: CanonicalDataset | undefined,
  current: ColumnMapping[]
): ColumnMapping[] {
  if (!dataset) return []
  return parseColumns(dataset.schemaColumnsJson).map((column) => {
    const existing = current.find((mapping) => mapping.target === column.name)
    return {
      target: column.name,
      targetType: column.polarsType,
      required: column.isRequired,
      sourceKind: existing?.sourceKind ?? "unmapped",
      sourceColumn: existing?.sourceColumn ?? "",
      constant: existing?.constant ?? "",
    }
  })
}

const targetAliases: Record<string, string[]> = {
  gross_amt: ["gross_amt", "gross", "gross amount", "tran amount", "trans_amount", "transaction amount", "original transaction amount", "charge_amount", "amount", "column_6"],
  gross_amount: ["gross_amount", "gross_amt", "gross", "gross amount", "tran amount", "trans_amount", "transaction amount", "original transaction amount", "charge_amount", "amount", "column_6"],
  charge_amount: ["charge_amount", "gross_amt", "gross", "amount", "tran amount", "trans_amount", "gross amount", "column_6"],
  amount: ["amount", "gross_amt", "gross", "charge_amount", "tran amount", "trans_amount", "column_6"],
  net_amt: ["net_amt", "net", "net amount", "deposit net amount", "settlement", "settlement amount", "amount paid", "amount due", "column_7"],
  net_amount: ["net_amount", "net_amt", "net", "deposit net amount", "settlement", "settlement amount", "amount paid", "column_7"],
  fee_amt: ["fee_amt", "mdr", "discount amount", "disc_amt", "fee", "fee amount", "commission", "tax amount", "column_15"],
  fee_amount: ["fee_amount", "fee_amt", "mdr", "discount amount", "disc_amt", "fee", "commission", "column_15"],
  trx_date: ["trx_date", "trans_date", "trandate", "tran date", "transaction date", "txn date", "sale_date", "post_date", "date", "column_1"],
  trans_date: ["trans_date", "trx_date", "trandate", "tran date", "transaction date", "txn date", "sale_date", "post_date", "date", "column_1"],
  sale_date: ["sale_date", "trx_date", "trans_date", "trandate", "transaction date", "txn date", "date", "column_1"],
  date: ["date", "trx_date", "trans_date", "trandate", "transaction date", "txn date", "sale_date", "column_1"],
  settle_date: ["settle_date", "settle date", "settlement date", "paymentdat", "payment date", "processing date", "value date", "column_2", "column_3"],
  settlement_date: ["settlement_date", "settle_date", "settle date", "paymentdat", "payment date", "processing date", "value date", "column_2", "column_3"],
  card_number: ["card_number", "card number", "card no./desc.", "card no", "accountno", "account number", "pan", "account", "card", "card_last_4", "card_pan_last4", "column_5"],
  card_last_4: ["card_last_4", "card_number", "card number", "card no./desc.", "card no", "accountno", "pan", "card_pan_last4", "card", "column_5"],
  card_pan_last4: ["card_pan_last4", "card_last_4", "card_number", "card number", "card no./desc.", "card no", "accountno", "pan", "column_5"],
  app_code: ["app_code", "authorization code", "approval code", "auth code", "auth", "approval", "auth_code", "approval_code", "app code", "column_8"],
  approval_code: ["approval_code", "app_code", "authorization code", "approval code", "auth code", "auth", "approval", "auth_code", "column_8"],
  auth_code: ["auth_code", "app_code", "authorization code", "approval code", "auth code", "auth", "approval", "approval_code", "column_8"],
  terminal_id: ["terminal_id", "terminal id", "eft_terminal", "tid", "pos_terminal", "column_17", "column_10"],
  pos_terminal: ["pos_terminal", "terminal_id", "terminal id", "eft_terminal", "tid", "column_17", "column_10"],
  rrn: ["rrn", "reference number", "reference", "reference no", "trace", "retrieval reference number", "ref_no", "column_9", "column_16"],
  order_id: ["order_id", "invoice_number", "order", "invoice", "receipt", "ext_ref", "seq_no", "ticket", "column_18"],
  ext_ref: ["ext_ref", "order_id", "invoice_number", "reference", "ref_no", "column_18"],
  merchant_code: ["merchant_code", "merchant number", "merchant_number", "merch_nbr", "mid", "column_4"],
  trx_number: ["trx_number", "transaction number", "trans_no"],
  batch_number: ["batch_number", "batch_no", "batch", "batch number"],
  batch_no: ["batch_no", "batch_number", "batch", "batch number"],
  store_id: ["store_id", "store", "branch", "branch_id", "location"],
}

function cleanStr(s: string) {
  return s.toLowerCase().replace(/[^a-z0-9]/g, "")
}

export function autoMap(
  mappings: ColumnMapping[],
  sources: string[]
): ColumnMapping[] {
  const usedSources = new Set<string>()

  return mappings.map((mapping) => {
    if (mapping.sourceKind === "constant") return mapping

    const targetLower = mapping.target.toLowerCase()
    const targetClean = cleanStr(mapping.target)

    // 1. Exact match (case-insensitive)
    let match = sources.find(
      (source) => source.toLowerCase() === targetLower && !usedSources.has(source)
    )

    // 2. Cleaned exact match (e.g. gross_amt vs grossamt, Card No. vs cardno)
    if (!match) {
      match = sources.find(
        (source) => cleanStr(source) === targetClean && !usedSources.has(source)
      )
    }

    // 3. Known semantic aliases
    if (!match) {
      const aliases = targetAliases[targetLower] || targetAliases[targetClean] || []
      for (const alias of aliases) {
        const found = sources.find(
          (source) =>
            (source.toLowerCase() === alias.toLowerCase() ||
              cleanStr(source) === cleanStr(alias)) &&
            !usedSources.has(source)
        )
        if (found) {
          match = found
          break
        }
      }
    }

    // 4. Substring inclusion match (e.g. "transaction date" contains "date")
    if (!match) {
      match = sources.find(
        (source) =>
          !source.startsWith("column_") &&
          !/(currency|curr\b|%|rate\b|time\b|name\b)/i.test(source) &&
          (cleanStr(source).includes(targetClean) ||
            targetClean.includes(cleanStr(source))) &&
          !usedSources.has(source)
      )
    }

    if (!match) return mapping

    usedSources.add(match)
    return { ...mapping, sourceKind: "column", sourceColumn: match }
  })
}

export function mappingComplete(mapping: ColumnMapping) {
  if (mapping.sourceKind === "column")
    return mapping.sourceColumn.trim().length > 0
  if (mapping.sourceKind === "constant") return true
  return false
}

export function compliance(mappings: ColumnMapping[]) {
  const required = mappings.filter((mapping) => mapping.required)
  const missing = required
    .filter((mapping) => !mappingComplete(mapping))
    .map((mapping) => mapping.target)
  return {
    totalRequired: required.length,
    satisfied: required.length - missing.length,
    missing,
    complete: missing.length === 0 && mappings.length > 0,
  }
}

export function parseSample(
  text: string,
  delimiter: string,
  headerMode: HeaderMode,
  headerRowIndex: number,
  customColumnNames?: string[]
) {
  const cleanDelimiter = delimiter || detectDelimiter(text)
  const rows = parseDelimited(
    text.replace(/^\uFEFF/, ""),
    cleanDelimiter || ","
  )
  if (rows.length === 0)
    return { columns: [] as SourceColumn[], sampleRows: [] as string[][] }
  const width = rows.reduce((max, row) => Math.max(max, row.length), 0)
  const padded = rows.map((row) => [
    ...row,
    ...Array(Math.max(0, width - row.length)).fill(""),
  ])

  let resolvedMode = headerMode
  let resolvedHeaderIndex = headerRowIndex
  if (resolvedMode !== "none") {
    const rawLines = text
      .split(/\r?\n/)
      .map((l) => l.trim())
      .filter(Boolean)
    const detected = detectHeaderMode(rawLines, cleanDelimiter)
    if (detected.headerMode === "none" || padded.length === 1) {
      resolvedMode = "none"
      resolvedHeaderIndex = 0
    }
  }

  if (resolvedMode === "none") {
    const sampleRows = padded.slice(0, 12)
    return {
      columns: Array.from({ length: width }, (_, index) => {
        const hasData = sampleRows.some((row) => (row[index] ?? "").trim() !== "")
        return {
          name: customColumnNames?.[index] || `column_${index + 1}`,
          included: hasData,
        }
      }),
      sampleRows,
      headerMode: "none" as HeaderMode,
      delimiter: cleanDelimiter,
    }
  }
  const headerAt =
    resolvedMode === "rowIndex"
      ? Math.min(resolvedHeaderIndex, padded.length - 1)
      : 0
  const header = padded[headerAt] ?? []
  const sampleRows = padded.slice(headerAt + 1, headerAt + 13)
  return {
    columns: header.map((name, index) => {
      const hasData = sampleRows.some((row) => (row[index] ?? "").trim() !== "")
      return {
        name: name || `column_${index + 1}`,
        included: hasData,
      }
    }),
    sampleRows,
    headerMode: resolvedMode,
    delimiter: cleanDelimiter,
  }
}

export function parseExcelSample(
  buffer: ArrayBuffer,
  sheetName?: string,
  headerMode: HeaderMode = "firstRow",
  headerRowIndex: number = 0
) {
  const workbook = XLSX.read(buffer, { type: "array" })
  const sheetNames = workbook.SheetNames || []
  const activeSheet =
    sheetName && sheetNames.includes(sheetName)
      ? sheetName
      : sheetNames[0] || "Sheet1"
  const worksheet = workbook.Sheets[activeSheet]
  if (!worksheet) {
    return {
      columns: [] as SourceColumn[],
      sampleRows: [] as string[][],
      sheetName: activeSheet,
      sheetNames,
      snippet: "",
    }
  }

  const rawRows: (string | number | boolean | null)[][] =
    XLSX.utils.sheet_to_json(worksheet, {
      header: 1,
      raw: false,
      defval: "",
      blankrows: true,
    })

  if (rawRows.length === 0) {
    return {
      columns: [] as SourceColumn[],
      sampleRows: [] as string[][],
      sheetName: activeSheet,
      sheetNames,
      snippet: "",
    }
  }

  const width = rawRows.reduce((max, r) => Math.max(max, r.length), 0)
  const padded: string[][] = rawRows.map((r) => [
    ...r.map((v) => (v != null ? String(v) : "")),
    ...Array(Math.max(0, width - r.length)).fill(""),
  ])

  let headerAt = 0
  if (headerMode === "rowIndex") {
    headerAt = Math.min(Math.max(0, headerRowIndex), padded.length - 1)
  } else if (headerMode === "firstRow") {
    headerAt = 0
    let bestScore = -1
    for (let r = 0; r < Math.min(padded.length, 30); r++) {
      const nonNull = padded[r].filter((v) => v.trim() !== "")
      const textLike = nonNull.filter(
        (v) =>
          !/^-?\d+(\.\d+)?$/.test(v) && !/^\d{4}-\d{2}-\d{2}/.test(v)
      )
      const score = nonNull.length * 2 + textLike.length
      if (score > bestScore && nonNull.length >= Math.min(width, 2)) {
        bestScore = score
        headerAt = r
      }
    }
  }

  let columns: SourceColumn[]
  let sampleRows: string[][] = []

  if (headerMode === "none") {
    sampleRows = padded.slice(0, 15)
    columns = Array.from({ length: width }, (_, index) => {
      const hasData = sampleRows.some((row) => (row[index] ?? "").trim() !== "")
      return {
        name: `column_${index + 1}`,
        included: hasData,
      }
    })
  } else {
    const headerRow = padded[headerAt] || []
    sampleRows = padded.slice(headerAt + 1, headerAt + 16)
    columns = headerRow.map((name, index) => {
      const hasData = sampleRows.some((row) => (row[index] ?? "").trim() !== "")
      return {
        name: name.trim() || `column_${index + 1}`,
        included: hasData,
      }
    })
  }

  const snippet = padded
    .slice(0, 8)
    .map((row) => row.map((c) => c.replace(/\t/g, " ")).join("\t"))
    .join("\n")

  return {
    columns,
    sampleRows,
    sheetName: activeSheet,
    sheetNames,
    snippet,
    headerRowIndex: headerAt,
  }
}

export function readStudio(
  pipelineJson: string
): { studio: StudioDocument; pipelineId: string; revision: number } | null {
  try {
    const root = JSON.parse(pipelineJson) as {
      pipeline?: { id?: string; revision?: number }
      steps?: {
        id?: string
        enabled?: boolean
        args?: { studio?: StudioDocument }
      }[]
    }
    const marker = root.steps?.find((step) => step.id === "__studio")
    if (!marker?.args?.studio) return null
    return {
      studio: { ...emptyStudio(), ...marker.args.studio },
      pipelineId: root.pipeline?.id || crypto.randomUUID(),
      revision: root.pipeline?.revision || 1,
    }
  } catch {
    return null
  }
}

export function compilePipeline(
  name: string,
  studio: StudioDocument,
  pipelineId: string,
  revision: number
) {
  const included = studio.columns.filter(
    (column) => column.included && column.name.trim()
  )
  const sourceNames = included.map((column) => column.name)
  const steps: Record<string, unknown>[] = []
  const keywords = studio.totalRowKeywords
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean)
  if (studio.removeTotalRows && keywords.length > 0 && sourceNames[0]) {
    steps.push(
      step(
        "drop-totals",
        "filter",
        "Drop total rows",
        predicateNotPrefix(sourceNames[0], keywords)
      )
    )
  }
  for (const item of studio.steps.filter((item) => item.enabled)) {
    if (item.kind === "scaleCents") {
      const colIdx = studio.columns.findIndex((c) => c.name === item.source)
      if (colIdx >= 0) {
        const sampleVals = studio.sampleRows
          .map((r) => r[colIdx] ?? "")
          .filter(Boolean)
        // If sample values already contain a decimal point (e.g. 1850.00), do NOT scale cents
        if (sampleVals.some((v) => /\.\d{1,4}$/.test(v.trim()))) {
          continue
        }
      }
    }
    if (item.kind === "parseDate") {
      const colIdx = studio.columns.findIndex((c) => c.name === item.source)
      if (colIdx >= 0) {
        const sampleVals = studio.sampleRows
          .map((r) => r[colIdx] ?? "")
          .filter(Boolean)
        // If column has no data or is already ISO YYYY-MM-DD, do not force non-standard parseDate
        if (
          sampleVals.length === 0 ||
          sampleVals.every((v) => /^\d{4}-\d{2}-\d{2}$/.test(v.trim()))
        ) {
          continue
        }
      }
    }
    const compiled = compileStep(item)
    if (compiled) steps.push(compiled)
  }
  const seenTargets = new Set<string>()
  const mapped = studio.mappings.filter((m) => {
    if (!mappingComplete(m) || seenTargets.has(m.target)) return false
    seenTargets.add(m.target)
    return true
  })
  let mappingPrefix = "__grid_mapping_"
  while (sourceNames.some((name) => name.startsWith(mappingPrefix)))
    mappingPrefix = `_${mappingPrefix}`
  for (const [index, mapping] of mapped.entries()) {
    const temporary = `${mappingPrefix}${index}`
    steps.push({
      id: `map-${index}`,
      op: "derive",
      enabled: true,
      args: {
        target: temporary,
        expression:
          mapping.sourceKind === "constant"
            ? { node: "literal", dataType: "string", value: mapping.constant }
            : { node: "column", name: mapping.sourceColumn },
      },
    })
    const dataType = dslType(mapping.targetType)
    steps.push({
      id: `cast-${index}`,
      op: "cast",
      enabled: true,
      args: {
        source: temporary,
        target: temporary,
        dataType,
        preprocess: { trim: true },
      },
    })
  }
  if (mapped.length > 0) {
    steps.push({
      id: "select-contract",
      op: "select",
      enabled: true,
      args: { columns: mapped.map((_, index) => `${mappingPrefix}${index}`) },
    })
    steps.push({
      id: "name-contract",
      op: "rename",
      enabled: true,
      args: {
        columns: Object.fromEntries(
          mapped.map((mapping, index) => [
            `${mappingPrefix}${index}`,
            mapping.target,
          ])
        ),
      },
    })
  }
  steps.push({
    id: "__studio",
    op: "profile",
    enabled: false,
    args: { studio },
  })

  const allColumnNames = studio.columns.map(
    (column, index) => column.name.trim() || `column_${index + 1}`
  )

  return {
    dslVersion: "2.0",
    pipeline: { id: pipelineId, revision, name: pipelineName(name) },
    inputs: [
      {
        id: "main",
        connector: connectorFor(studio.expectedFormat),
        binding: "execution.input",
        options: inputOptions(studio, allColumnNames),
      },
    ],
    steps,
    quality: {
      defaultErrorAction: "quarantine",
      failExecutionWhen: { quarantinePercentageGreaterThan: 0 },
      quarantine: { enabled: true, relativePath: "_quarantine" },
    },
    sink: {
      connector: "parquet",
      binding: "execution.output",
      writeMode: "overwrite",
      partitionBy: [],
    },
  }
}

export function datasetPartitions(dataset: CanonicalDataset | undefined) {
  return dataset ? parsePartitions(dataset.partitionColumnsJson) : []
}

function compileStep(item: StudioStep): Record<string, unknown> | null {
  const source = item.source
  if (!source && item.kind !== "filterPrefix") return null
  if (item.kind === "parseDate") {
    return step(item.id, "parseDateTime", item.label, {
      source,
      target: source,
      format: item.params.format || "%Y-%m-%d",
      dataType: "date",
      exact: true,
    })
  }
  if (item.kind === "maskCard") {
    return step(item.id, "mask", item.label, {
      source,
      target: source,
      keepStart: 6,
      keepEnd: 4,
      maskCharacter: "*",
    })
  }
  if (item.kind === "scaleCents") {
    return step(item.id, "derive", item.label, {
      target: source,
      expression: {
        node: "arithmetic",
        operator: "divide",
        left: { node: "column", name: source },
        right: { node: "literal", dataType: "decimal(18,2)", value: 100 },
      },
    })
  }
  if (item.kind === "pad") {
    return step(item.id, "textFormat", item.label, {
      columns: [source],
      mode: "pad",
      length: Number(item.params.length || 0),
      padChar: item.params.padChar || "0",
      side: item.params.side || "left",
    })
  }
  if (item.kind === "trimChars") {
    return step(item.id, "trimCharacters", item.label, {
      source,
      target: source,
      characters: item.params.characters || "0",
      side: item.params.side || "start",
    })
  }
  if (item.kind === "extract") {
    return step(item.id, "substring", item.label, {
      source,
      target: source,
      offset: Number(item.params.offset || 0),
      length: Number(item.params.length || 1),
    })
  }
  if (item.kind === "replace") {
    return step(item.id, "replaceText", item.label, {
      source,
      target: source,
      pattern: item.params.pattern || "",
      replacement: item.params.replacement || "",
      regex: false,
      all: true,
    })
  }
  if (item.kind === "trim") {
    return step(item.id, "textFormat", item.label, {
      columns: [source],
      mode: "trim",
    })
  }
  if (item.kind === "case") {
    const branches = (item.params.rules || "")
      .split("\n")
      .map((line) => line.split("=>"))
      .filter((parts) => parts.length === 2 && parts[0]?.trim())
      .map(([when, then]) => ({
        when: {
          node: "compare",
          operator: "eq",
          left: { node: "column", name: source },
          right: {
            node: "literal",
            dataType: "string",
            value: when?.trim() ?? "",
          },
        },
        then: {
          node: "literal",
          dataType: "string",
          value: then?.trim() ?? "",
        },
      }))
    if (branches.length === 0) return null
    return step(item.id, "derive", item.label, {
      target: source,
      expression: {
        node: "case",
        branches,
        otherwise: { node: "column", name: source },
      },
    })
  }
  const keywords = (item.params.keywords || "")
    .split(",")
    .map((value) => value.trim())
    .filter(Boolean)
  if (!source || keywords.length === 0) return null
  return step(
    item.id,
    "filter",
    item.label,
    predicateNotPrefix(source, keywords)
  )
}

function predicateNotPrefix(column: string, keywords: string[]) {
  return {
    predicate: {
      node: "not",
      expression: {
        node: "any",
        children: keywords.map((keyword) => ({
          node: "compare",
          operator: "startsWith",
          caseSensitive: false,
          left: { node: "column", name: column },
          right: { node: "literal", dataType: "string", value: keyword },
        })),
      },
    },
  }
}

function step(
  id: string,
  op: string,
  label: string,
  args: Record<string, unknown>
) {
  return { id, op, enabled: true, label, args }
}

function inputOptions(studio: StudioDocument, sourceNames: string[]) {
  if (studio.expectedFormat === "excel") {
    const resolvedSkip =
      studio.headerMode === "rowIndex"
        ? studio.headerRowIndex
        : (studio.skipRows || (studio.headerMode === "firstRow" ? studio.headerRowIndex : 0))
    return {
      sheetName: studio.sheetName || null,
      hasHeader: studio.headerMode !== "none",
      headerRowIndex: studio.headerRowIndex || resolvedSkip || 0,
      skipRows: resolvedSkip,
      columnNames: sourceNames,
    }
  }
  if (studio.expectedFormat === "fixedWidth") {
    return {
      columns: studio.columns
        .filter((column) => column.included)
        .map((column) => ({
          name: column.name,
          start: column.start ?? 0,
          length: column.length ?? 1,
        })),
    }
  }
  return {
    delimiter: studio.delimiter || ",",
    hasHeader: studio.headerMode !== "none",
    headerMode: studio.headerMode,
    headerRowIndex: studio.headerRowIndex,
    skipRows:
      studio.headerMode === "rowIndex"
        ? studio.headerRowIndex
        : studio.skipRows,
    columnNames: studio.headerMode === "none" ? sourceNames : [],
    encoding: "utf8",
  }
}

function connectorFor(format: SourceFormat) {
  if (format === "excel") return "excel"
  if (format === "fixedWidth") return "fixedWidth"
  return "csv"
}

function pipelineName(name: string) {
  const slugName = name
    .trim()
    .replace(/[^A-Za-z0-9_.-]/g, "_")
    .replace(/^[^A-Za-z_]+/, "")
  return slugName || "parser"
}

function dslType(polarsType: string) {
  const types: Record<string, string> = {
    "pl.String": "string",
    "pl.Date": "date",
    "pl.Datetime": "datetime",
    "pl.Decimal(18,2)": "decimal(18,2)",
    "pl.Int64": "int64",
    "pl.Int32": "int32",
    "pl.Float64": "float64",
    "pl.Boolean": "boolean",
  }
  return types[polarsType] ?? "string"
}

function parseDelimited(text: string, delimiter: string) {
  const rows: string[][] = []
  let cells: string[] = []
  let current = ""
  let quoted = false
  for (let index = 0; index < text.length; index += 1) {
    const char = text[index]
    if (char === '"') {
      if (quoted && text[index + 1] === '"') {
        current += '"'
        index += 1
        continue
      }
      quoted = !quoted
      continue
    }
    if (!quoted && char === delimiter) {
      cells.push(current)
      current = ""
      continue
    }
    if (!quoted && (char === "\n" || char === "\r")) {
      if (char === "\r" && text[index + 1] === "\n") index += 1
      cells.push(current)
      if (cells.some((cell) => cell.length > 0)) rows.push(cells)
      cells = []
      current = ""
      continue
    }
    current += char
  }
  if (quoted) throw new Error("CSV has an unterminated quoted field.")
  cells.push(current)
  if (cells.some((cell) => cell.length > 0)) rows.push(cells)
  return rows
}
