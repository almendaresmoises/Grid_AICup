import type { Package } from "@/lib/types"

export type ConfigurationValidation = {
  passed: boolean
  revision: string
  errors: string[]
  warnings: string[]
  columns: {
    dataset: string
    column: string
    missing: number
    distinct: number
    total: number
  }[]
  passes: { rule: string; groups: number; records: number }[]
  totalRecords: number
  fullMatchRecords: number
  partialMatchRecords: number
  exceptionRecords: number
}

export type ConfigurationJob = {
  id: string
  status: "WaitingForParsers" | "Completed" | "Failed"
  error?: string
  source: string
  package?: Package
  validation?: ConfigurationValidation
  files: {
    datasetCode: string
    fileName: string
    parserCode: string
    parserId: string
    runId: string
  }[]
}
