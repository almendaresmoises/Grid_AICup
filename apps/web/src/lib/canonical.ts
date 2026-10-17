export type CanonicalDataset = {
  id: string
  code: string
  name: string
  description: string
  gcsPrefix: string
  outputUri: string | null
  schemaColumnsJson: string
  partitionColumnsJson: string
  createdWithAi?: boolean
}

export type ParserConfiguration = {
  id: string
  name: string
  fileFormat: string
  pipelineJson: string
  outputGcsPrefix: string
  isActive: boolean
  createdWithAi?: boolean
}

export type ParserRun = {
  id: string
  parserConfigurationId: string
  status: "Queued" | "Succeeded" | "Failed"
  fileName: string
  inputObject: string
  outputObject: string
  error: string | null
  reportJson: string | null
  creationTime: string
}

export type SchemaColumn = {
  name: string
  polarsType: string
  isRequired: boolean
  semanticRole: string
}

export const columnTypes = [
  "pl.String",
  "pl.Date",
  "pl.Datetime",
  "pl.Decimal(18,2)",
  "pl.Int64",
  "pl.Int32",
  "pl.Float64",
  "pl.Boolean",
] as const

export const semanticRoles = [
  { value: "none", label: "None" },
  { value: "business_date", label: "Business date" },
  { value: "company_code", label: "Company code" },
  { value: "branch_code", label: "Branch code" },
  { value: "record_id", label: "Record id" },
] as const

export function parseColumns(json: string): SchemaColumn[] {
  try {
    const value = JSON.parse(json) as unknown
    if (!Array.isArray(value)) return []
    return value.flatMap((item) => {
      if (!item || typeof item !== "object") return []
      const column = item as Record<string, unknown>
      const name = typeof column.name === "string" ? column.name : ""
      const polarsType =
        typeof column.polarsType === "string" ? column.polarsType : "pl.String"
      const isRequired =
        typeof column.isRequired === "boolean" ? column.isRequired : true
      const semanticRole =
        typeof column.semanticRole === "string" ? column.semanticRole : "none"
      return [{ name, polarsType, isRequired, semanticRole }]
    })
  } catch {
    return []
  }
}

export function parsePartitions(json: string): string[] {
  try {
    const value = JSON.parse(json) as unknown
    return Array.isArray(value)
      ? value.filter((item): item is string => typeof item === "string")
      : []
  } catch {
    return []
  }
}

export function starterPipeline(name: string) {
  const pipelineName =
    name
      .trim()
      .replace(/[^A-Za-z0-9_.-]/g, "_")
      .replace(/^[^A-Za-z_]+/, "") || "parser"
  return JSON.stringify(
    {
      dslVersion: "2.0",
      pipeline: { id: crypto.randomUUID(), revision: 1, name: pipelineName },
      inputs: [
        {
          id: "main",
          connector: "csv",
          binding: "execution.input",
          options: { hasHeader: true, delimiter: "," },
        },
      ],
      steps: [],
      quality: {
        defaultErrorAction: "quarantine",
        quarantine: { enabled: true, relativePath: "_quarantine" },
      },
      sink: {
        connector: "parquet",
        binding: "execution.output",
        writeMode: "overwrite",
      },
    },
    null,
    2
  )
}
