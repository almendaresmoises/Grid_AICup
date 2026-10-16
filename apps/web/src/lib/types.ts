export type DatasetColumn = {
  name: string
  type: "text" | "amount" | "date" | string
}

export type Dataset = {
  id: string
  code: string
  name: string
  rowCount: number
  columns: DatasetColumn[]
  sampleRows?: Record<string, string>[] | null
}

export type Package = {
  id: string
  name: string
  description?: string | null
  profileId: string
  profileName: string
  profileVersion: number
  profileStatus: string
  creationTime: string
}

export type Profile = {
  id: string
  packageId: string
  packageName: string
  name: string
  version: number
  status: string
  definitionJson: string
  approvedDefinitionJson?: string | null
  hasBaseline: boolean
}

export type KeySpec = {
  type: "text" | "amount" | "date"
  fields: Record<string, string>
  strip_prefixes?: Record<string, string[]>
}

export type RuleSpec = {
  id: string
  type: "exact" | "aggregate"
  datasets: string[]
  keys: KeySpec[]
  target?: string
  source?: string
  target_amount?: string
  source_amount?: string
  tolerance?: string
}

export type ProfileSpec = {
  name: string
  version: number
  datasets: string[]
  rules: RuleSpec[]
}

export type RunSummary = {
  id: string
  profileId: string
  profileVersion: number
  status: string
  creationTime: string
  totalRecords: number
  matchedRecords: number
  unresolvedRecords: number
  ambiguousRecords: number
  matchedGroups: number
}

export type Evidence = {
  comparison: string
  target?: string | null
  sourceSum?: string | null
  variance?: string | null
}

export type MatchGroup = {
  rule: string
  key: string[]
  records: Record<string, number[]>
  evidence: Evidence
}

export type UnresolvedRow = {
  dataset: string
  csvRow: number
  reason: string
  record: Record<string, string>
}

export type RunDetail = RunSummary & {
  groups: MatchGroup[]
  unresolved: UnresolvedRow[]
}

export type Investigation = {
  diagnosis: string
  hypotheses: string[]
  proposedDefinitionJson: string
  changeSummary: string[]
  risks: string[]
}

export type Statistics = {
  totalRecords: number
  matchedRecords: number
  unresolvedRecords: number
  ambiguousRecords: number
  matchedGroups: number
}

export type RegressionPreview = {
  passed: boolean
  hasBaseline: boolean
  message: string
  currentProfile?: Statistics | null
  proposedOnCurrent?: Statistics | null
  baselineProfile?: Statistics | null
  proposedOnBaseline?: Statistics | null
}

export function emptyProfile(name = "New profile"): ProfileSpec {
  return { name, version: 0, datasets: [], rules: [] }
}

export function parseProfile(json: string): ProfileSpec {
  const value = JSON.parse(json) as ProfileSpec
  return {
    name: value.name ?? "",
    version: value.version ?? 0,
    datasets: value.datasets ?? [],
    rules: (value.rules ?? []).map((rule) => ({
      ...rule,
      datasets: rule.datasets ?? [],
      keys: (rule.keys ?? []).map((key) => ({
        type: key.type,
        fields: key.fields ?? {},
        strip_prefixes: key.strip_prefixes,
      })),
    })),
  }
}
