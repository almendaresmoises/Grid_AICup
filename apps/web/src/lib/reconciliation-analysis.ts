import { api } from "./api"

export interface StartOrResumeAnalysisInput {
  requestId: string
  profileId: string
  sourceRunId?: string
  context?: string
  expectedProfileRevision?: string
}

export interface StartOrResumeAnalysisResult {
  analysisId: string
  reused: boolean
  status: string
  message?: string
  revision: string
}

export interface DiagnosisItem {
  category: string
  summary: string
  affectedRecordCount: number
  sampleEvidenceIds: string[]
}

export interface AnalysisChange {
  id: string
  dependencyGroupId: string
  changeType: string
  targetIdentifier: string
  targetObjectVersion?: number
  operationType: string
  operationJson: string
  beforeJson: string
  afterJson: string
  rationale: string
  evidenceIds: string[]
  safetyClass: string
  isSelectable: boolean
  blockerReason?: string
}

export interface RegressionVerdict {
  runId: string
  description: string
  passed: boolean
  reason: string
  totalRecords: number
  matchedRecords: number
}

export interface DatasetBreakdown {
  datasetCode: string
  totalRecords: number
  baseFullMatchRecords: number
  basePartialMatchRecords: number
  baseUnresolvedRecords: number
  baseAmbiguousRecords: number
  candidateFullMatchRecords: number
  candidatePartialMatchRecords: number
  candidateUnresolvedRecords: number
  candidateAmbiguousRecords: number
}

export interface GroupContribution {
  groupId: string
  newlyMatchedRecords: number
  newlyMatchedGroups: number
  fullMatches: number
  partialMatches: number
  independentAnyGainPoints: number
  marginalAnyGainPoints: number
}

export interface AnalysisImpactSummary {
  totalRecords: number
  baseFullMatchRecords: number
  basePartialMatchRecords: number
  baseUnresolvedRecords: number
  baseAmbiguousRecords: number
  baseMatchedGroups: number

  candidateFullMatchRecords: number
  candidatePartialMatchRecords: number
  candidateUnresolvedRecords: number
  candidateAmbiguousRecords: number
  candidateMatchedGroups: number

  recoveredUnresolvedRecords: number
  protectedMatchedRecords: number

  baseFullMatchPercentage: number
  basePartialMatchPercentage: number
  baseAnyMatchPercentage: number

  candidateFullMatchPercentage: number
  candidatePartialMatchPercentage: number
  candidateAnyMatchPercentage: number

  fullImprovementPercentagePoints: number
  anyImprovementPercentagePoints: number
  recoveredUnresolvedPercentage: number

  preservedMatches: boolean
  preservedGroupsCount: number
  lostGroupsCount: number
  degradedGroupsCount: number
  changedMembersGroupsCount: number
  preservationViolations: string[]

  coverage: string
  regressionVerdicts: RegressionVerdict[]
  datasetBreakdowns: DatasetBreakdown[]
  groupContributions: GroupContribution[]
}

export interface ReconciliationAnalysisDetail {
  id: string
  profileId: string
  profileName: string
  profileVersion: number
  sourceRunId: string
  sourceRunTime?: string
  sourceRunTotalRecords: number
  sourceRunMatchedRecords: number
  sourceRunUnresolvedRecords: number
  datasets: string[]

  status: string
  stage: string
  creationTime: string
  completionTime?: string
  revision: string
  context?: string

  selectedGroupIds: string[]
  selectionHash: string
  impact?: AnalysisImpactSummary
  diagnoses: DiagnosisItem[]

  configurationCommitId?: string
  committedProfileVersion?: number
  executionId?: string
  executionStatus?: string
  resultingRunId?: string

  failureCode?: string
  failureMessage?: string

  canAccept: boolean
  canReject: boolean
  canRevalidate: boolean
  canRetry: boolean
  canRun: boolean
  blockerReason?: string
}

export interface ProfileAnalysisSummary {
  profileId: string
  activeAnalysisId?: string
  activeStatus?: string
  activeStage?: string
  lastAnalysisId?: string
  lastStatus?: string
  activeCommitId?: string
  activeCommitVersion?: number
  latestRunId?: string
  hasSuccessfulRun: boolean
  actionLabel: string
  actionType: "start" | "reopen" | "review" | "view_accepted" | "view_run" | "view_history" | "no_run"
  explanation?: string
}

export interface UpdateAnalysisSelectionInput {
  expectedRevision: string
  selectedGroupIds: string[]
}

export interface ReconciliationAnalysisDecisionInput {
  requestId: string
  expectedRevision: string
  decision: "AcceptSelected" | "RejectAll"
  selectionHash: string
  validationId?: string
  validationHash?: string
  materialAcknowledgements?: string[]
}

export interface DecisionResult {
  analysisId: string
  decision: string
  status: string
  commitId?: string
  committedProfileVersion?: number
}

export interface ExecuteAnalysisInput {
  requestId: string
  commitId: string
  inputMode?: string
  expectedCommitHash?: string
}

export interface ConfigurationExecution {
  executionId: string
  commitId: string
  status: string
  stage: string
  resultingRunId?: string
  errorMessage?: string
  attempt: number
}

export const reconciliationAnalysisKeys = {
  all: ["reconciliation-analysis"] as const,
  profileStatuses: (profileIds: string[]) =>
    ["reconciliation-analysis", "profile-statuses", profileIds.slice().sort().join(",")] as const,
  detail: (id: string) => ["reconciliation-analysis", "detail", id] as const,
  changes: (id: string) => ["reconciliation-analysis", "changes", id] as const,
  execution: (id: string) => ["reconciliation-analysis", "execution", id] as const,
}

export async function startOrResumeAnalysis(
  input: StartOrResumeAnalysisInput
): Promise<StartOrResumeAnalysisResult> {
  return api<StartOrResumeAnalysisResult>("/api/app/reconciliation-analysis/start-or-resume", {
    method: "POST",
    body: JSON.stringify(input),
  })
}

export async function getAnalysis(id: string): Promise<ReconciliationAnalysisDetail> {
  return api<ReconciliationAnalysisDetail>(`/api/app/reconciliation-analysis/${id}`)
}

export async function getAnalysisChanges(id: string): Promise<AnalysisChange[]> {
  return api<AnalysisChange[]>(`/api/app/reconciliation-analysis/${id}/changes`)
}

export async function getProfileStatuses(
  profileIds: string[]
): Promise<ProfileAnalysisSummary[]> {
  if (!profileIds.length) return []
  return api<ProfileAnalysisSummary[]>("/api/app/reconciliation-analysis/profile-statuses", {
    method: "POST",
    body: JSON.stringify({ profileIds }),
  })
}

export async function updateAnalysisSelection(
  id: string,
  input: UpdateAnalysisSelectionInput
): Promise<ReconciliationAnalysisDetail> {
  return api<ReconciliationAnalysisDetail>(`/api/app/reconciliation-analysis/${id}/selection`, {
    method: "PUT",
    body: JSON.stringify(input),
  })
}

export async function revalidateAnalysis(
  id: string
): Promise<ReconciliationAnalysisDetail> {
  return api<ReconciliationAnalysisDetail>(`/api/app/reconciliation-analysis/${id}/revalidate`, {
    method: "POST",
  })
}

export async function submitAnalysisDecision(
  id: string,
  input: ReconciliationAnalysisDecisionInput
): Promise<DecisionResult> {
  return api<DecisionResult>(`/api/app/reconciliation-analysis/${id}/decision`, {
    method: "POST",
    body: JSON.stringify(input),
  })
}

export async function retryAnalysis(
  id: string
): Promise<ReconciliationAnalysisDetail> {
  return api<ReconciliationAnalysisDetail>(`/api/app/reconciliation-analysis/${id}/retry`, {
    method: "POST",
  })
}

export async function executeAnalysis(
  id: string,
  input: ExecuteAnalysisInput
): Promise<ConfigurationExecution> {
  return api<ConfigurationExecution>(`/api/app/reconciliation-analysis/${id}/run`, {
    method: "POST",
    body: JSON.stringify(input),
  })
}

export async function getAnalysisExecution(
  id: string
): Promise<ConfigurationExecution> {
  return api<ConfigurationExecution>(`/api/app/reconciliation-analysis/${id}/execution`)
}
