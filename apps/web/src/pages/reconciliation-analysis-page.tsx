import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Button } from "@workspace/ui/components/button"
import {
  AlertCircle,
  AlertOctagon,
  AlertTriangle,
  ArrowLeft,
  CheckCircle,
  CheckCircle2,
  ChevronDown,
  ChevronRight,
  FileDiff,
  Loader2,
  Play,
  RotateCcw,
  ShieldAlert,
  ShieldCheck,
  XCircle,
} from "lucide-react"
import { useMemo, useState } from "react"
import { Link, useNavigate, useParams } from "react-router"
import { ErrorNote } from "@/components/error-note"
import {
  executeAnalysis,
  getAnalysis,
  getAnalysisChanges,
  getAnalysisExecution,
  reconciliationAnalysisKeys,
  retryAnalysis,
  revalidateAnalysis,
  submitAnalysisDecision,
  updateAnalysisSelection,
  type AnalysisChange,
} from "@/lib/reconciliation-analysis"

export function ReconciliationAnalysisPage() {
  const { profileId = "", analysisId = "" } = useParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [expandedDiffs, setExpandedDiffs] = useState<Record<string, boolean>>({})
  const [showMaterialConfirm, setShowMaterialConfirm] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  const analysisQuery = useQuery({
    queryKey: reconciliationAnalysisKeys.detail(analysisId),
    queryFn: () => getAnalysis(analysisId),
    refetchInterval: (query) => {
      const data = query.state.data
      if (!data) return 3000
      if (["Queued", "Analyzing", "Validating"].includes(data.status)) {
        return 3000
      }
      return false
    },
  })

  const changesQuery = useQuery({
    queryKey: reconciliationAnalysisKeys.changes(analysisId),
    queryFn: () => getAnalysisChanges(analysisId),
    enabled: Boolean(analysisId),
  })

  const executionQuery = useQuery({
    queryKey: reconciliationAnalysisKeys.execution(analysisId),
    queryFn: () => getAnalysisExecution(analysisId),
    enabled: Boolean(analysisQuery.data?.executionId),
    refetchInterval: (query) => {
      const data = query.state.data
      if (data && ["Queued", "PreparingInputs", "Reconciling"].includes(data.status)) {
        return 3000
      }
      return false
    },
  })

  const selectionMutation = useMutation({
    mutationFn: (selectedGroupIds: string[]) =>
      updateAnalysisSelection(analysisId, {
        expectedRevision: analysisQuery.data?.revision ?? "",
        selectedGroupIds,
      }),
    onSuccess: (data) => {
      setActionError(null)
      queryClient.setQueryData(reconciliationAnalysisKeys.detail(analysisId), data)
      queryClient.invalidateQueries({ queryKey: reconciliationAnalysisKeys.detail(analysisId) })
    },
    onError: (err) => {
      setActionError(err instanceof Error ? err.message : "Failed to update selection")
    },
  })

  const decisionMutation = useMutation({
    mutationFn: (decision: "AcceptSelected" | "RejectAll") =>
      submitAnalysisDecision(analysisId, {
        requestId: crypto.randomUUID(),
        expectedRevision: analysisQuery.data?.revision ?? "",
        decision,
        selectionHash: analysisQuery.data?.selectionHash ?? "",
        materialAcknowledgements: showMaterialConfirm ? ["acknowledge_material_change"] : [],
      }),
    onSuccess: () => {
      setActionError(null)
      setShowMaterialConfirm(false)
      queryClient.invalidateQueries({ queryKey: reconciliationAnalysisKeys.detail(analysisId) })
      queryClient.invalidateQueries({ queryKey: ["packages"] })
      queryClient.invalidateQueries({ queryKey: ["profile-registry"] })
    },
    onError: (err) => {
      setActionError(err instanceof Error ? err.message : "Decision failed")
    },
  })

  const revalidateMutation = useMutation({
    mutationFn: () => revalidateAnalysis(analysisId),
    onSuccess: (data) => {
      setActionError(null)
      queryClient.setQueryData(reconciliationAnalysisKeys.detail(analysisId), data)
      queryClient.invalidateQueries({ queryKey: reconciliationAnalysisKeys.detail(analysisId) })
    },
    onError: (err) => {
      setActionError(err instanceof Error ? err.message : "Revalidation failed")
    },
  })

  const retryMutation = useMutation({
    mutationFn: () => retryAnalysis(analysisId),
    onSuccess: (data) => {
      setActionError(null)
      queryClient.setQueryData(reconciliationAnalysisKeys.detail(analysisId), data)
      queryClient.invalidateQueries({ queryKey: reconciliationAnalysisKeys.detail(analysisId) })
    },
    onError: (err) => {
      setActionError(err instanceof Error ? err.message : "Retry failed")
    },
  })

  const executeMutation = useMutation({
    mutationFn: () =>
      executeAnalysis(analysisId, {
        requestId: crypto.randomUUID(),
        commitId: analysisQuery.data?.configurationCommitId ?? "",
      }),
    onSuccess: () => {
      setActionError(null)
      queryClient.invalidateQueries({ queryKey: reconciliationAnalysisKeys.detail(analysisId) })
      queryClient.invalidateQueries({ queryKey: reconciliationAnalysisKeys.execution(analysisId) })
    },
    onError: (err) => {
      setActionError(err instanceof Error ? err.message : "Execution failed")
    },
  })

  const analysis = analysisQuery.data
  const impact = analysis?.impact

  // Group changes by dependencyGroupId
  const groupedChanges = useMemo(() => {
    const rawChanges = changesQuery.data ?? []
    const map = new Map<string, AnalysisChange[]>()
    for (const change of rawChanges) {
      const group = map.get(change.dependencyGroupId) ?? []
      group.push(change)
      map.set(change.dependencyGroupId, group)
    }
    return Array.from(map.entries()).map(([groupId, items]) => ({
      groupId,
      items,
      isSelectable: items.every((i) => i.isSelectable),
      hasMaterialChange: items.some((i) => i.safetyClass === "material_change"),
      blockerReason: items.find((i) => i.blockerReason)?.blockerReason,
    }))
  }, [changesQuery.data])

  const selectedGroupIds = analysis?.selectedGroupIds ?? []

  const toggleGroup = (groupId: string) => {
    if (!analysis || analysis.status !== "AwaitingDecision") return
    const next = selectedGroupIds.includes(groupId)
      ? selectedGroupIds.filter((id) => id !== groupId)
      : [...selectedGroupIds, groupId]
    selectionMutation.mutate(next)
  }

  const toggleDiff = (changeId: string) => {
    setExpandedDiffs((prev) => ({ ...prev, [changeId]: !prev[changeId] }))
  }

  if (analysisQuery.isLoading) {
    return (
      <div className="flex flex-col items-center justify-center py-20 text-center">
        <Loader2 className="size-8 animate-spin text-primary mb-3" />
        <h2 className="text-base font-medium">Loading reconciliation optimization assessment...</h2>
      </div>
    )
  }

  if (analysisQuery.error || !analysis) {
    return (
      <div className="flex flex-col gap-4">
        <Button variant="outline" size="sm" className="w-fit" render={<Link to={`/reconciliation/profiles/${profileId}`} />}>
          <ArrowLeft className="size-4" /> Back to profile
        </Button>
        <ErrorNote message={analysisQuery.error instanceof Error ? analysisQuery.error.message : "Assessment not found"} />
      </div>
    )
  }

  const isWorking = ["Queued", "Analyzing", "Validating"].includes(analysis.status)
  const isAwaitingDecision = analysis.status === "AwaitingDecision"
  const isAccepted = analysis.status === "Accepted"
  const isRejected = analysis.status === "Rejected"
  const isNoRecs = analysis.status === "NoRecommendations"
  const isFailed = analysis.status === "Failed"
  const isStaleOrBlocked = ["Stale", "Blocked"].includes(analysis.status)

  const hasSelectedMaterial = groupedChanges.some(
    (g) => selectedGroupIds.includes(g.groupId) && g.hasMaterialChange
  )

  return (
    <div className="flex flex-col gap-6 max-w-6xl mx-auto pb-16">
      {/* Top Navigation */}
      <div className="flex items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <Button
            variant="outline"
            size="sm"
            render={<Link to={`/reconciliation/profiles/${profileId}`} />}
          >
            <ArrowLeft className="size-4" /> Profile Studio
          </Button>
          <span className="text-muted-foreground text-sm">/</span>
          <span className="text-sm font-medium">AI Optimization Assessment</span>
        </div>
        <div className="flex items-center gap-2">
          {isStaleOrBlocked && analysis.canRevalidate ? (
            <Button
              variant="outline"
              size="sm"
              disabled={revalidateMutation.isPending}
              onClick={() => revalidateMutation.mutate()}
            >
              <RotateCcw className="size-3.5" /> Revalidate
            </Button>
          ) : null}
          {isFailed && analysis.canRetry ? (
            <Button
              variant="outline"
              size="sm"
              disabled={retryMutation.isPending}
              onClick={() => retryMutation.mutate()}
            >
              <RotateCcw className="size-3.5" /> Retry
            </Button>
          ) : null}
        </div>
      </div>

      {actionError ? <ErrorNote message={actionError} /> : null}

      {/* Header Info Banner */}
      <div className="border bg-card rounded-lg p-5 flex flex-col gap-4">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <h1 className="text-xl font-semibold flex items-center gap-2">
              <span>{analysis.profileName}</span>
              <span className="text-xs font-normal border rounded px-2 py-0.5 bg-muted/40">
                v{analysis.profileVersion}
              </span>
            </h1>
            <p className="text-xs text-muted-foreground mt-1">
              Assessed on source run <code className="font-mono">{analysis.sourceRunId.slice(0, 8)}</code>
              {analysis.sourceRunTime ? ` · ${new Date(analysis.sourceRunTime).toLocaleString()}` : ""}
            </p>
          </div>

          <div className="flex items-center gap-2">
            <span
              className={`inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-medium border ${
                isAccepted
                  ? "bg-green-500/10 text-green-700 border-green-500/30 dark:text-green-400"
                  : isRejected
                  ? "bg-red-500/10 text-red-700 border-red-500/30 dark:text-red-400"
                  : isAwaitingDecision
                  ? "bg-blue-500/10 text-blue-700 border-blue-500/30 dark:text-blue-400"
                  : isWorking
                  ? "bg-amber-500/10 text-amber-700 border-amber-500/30 dark:text-amber-400"
                  : "bg-muted text-muted-foreground"
              }`}
            >
              {isWorking ? <Loader2 className="size-3.5 animate-spin" /> : null}
              {isAccepted ? <ShieldCheck className="size-3.5" /> : null}
              {isRejected ? <XCircle className="size-3.5" /> : null}
              {isAwaitingDecision ? <CheckCircle2 className="size-3.5" /> : null}
              {isNoRecs ? <AlertTriangle className="size-3.5" /> : null}
              {isFailed ? <AlertOctagon className="size-3.5" /> : null}
              {isStaleOrBlocked ? <AlertCircle className="size-3.5" /> : null}
              <span>{analysis.status}</span>
              {analysis.stage ? <span className="opacity-70">({analysis.stage})</span> : null}
            </span>
          </div>
        </div>

        {/* Source Run Stats & Scope */}
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 pt-3 border-t text-xs">
          <div>
            <span className="text-muted-foreground">Source Transactions</span>
            <p className="font-semibold text-sm tabular-nums">{analysis.sourceRunTotalRecords.toLocaleString()}</p>
          </div>
          <div>
            <span className="text-muted-foreground">Baseline Matched</span>
            <p className="font-semibold text-sm tabular-nums text-green-700 dark:text-green-400">
              {analysis.sourceRunMatchedRecords.toLocaleString()}
            </p>
          </div>
          <div>
            <span className="text-muted-foreground">Baseline Unresolved</span>
            <p className="font-semibold text-sm tabular-nums text-amber-700 dark:text-amber-400">
              {analysis.sourceRunUnresolvedRecords.toLocaleString()}
            </p>
          </div>
          <div>
            <span className="text-muted-foreground">Participating Datasets</span>
            <p className="font-semibold text-sm">{analysis.datasets.join(", ") || "—"}</p>
          </div>
        </div>

        {analysis.context ? (
          <div className="bg-muted/30 border rounded p-3 text-xs">
            <span className="font-medium text-muted-foreground">Business Context:</span>
            <p className="mt-0.5 text-foreground">{analysis.context}</p>
          </div>
        ) : null}

        {analysis.failureMessage ? (
          <div className="bg-red-500/10 border border-red-500/30 text-red-700 dark:text-red-400 rounded p-3 text-xs">
            <span className="font-semibold">Assessment Error: </span>
            {analysis.failureMessage}
          </div>
        ) : null}

        {analysis.blockerReason ? (
          <div className="bg-amber-500/10 border border-amber-500/30 text-amber-800 dark:text-amber-300 rounded p-3 text-xs flex items-start gap-2">
            <AlertTriangle className="size-4 shrink-0 mt-0.5" />
            <div>
              <span className="font-semibold">Blocker: </span>
              {analysis.blockerReason}
            </div>
          </div>
        ) : null}
      </div>

      {/* Progress View for active work */}
      {isWorking ? (
        <div className="border bg-card rounded-lg p-8 flex flex-col items-center justify-center text-center gap-3">
          <Loader2 className="size-10 animate-spin text-primary" />
          <h3 className="text-base font-medium">
            {analysis.status === "Queued" && "Analysis Queued in Background"}
            {analysis.status === "Analyzing" && "Analyzing Unresolved Records with AI Advisor..."}
            {analysis.status === "Validating" && "Replaying Deterministic Matcher & Verifying Regression..."}
          </h3>
          <p className="text-xs text-muted-foreground max-w-md">
            The assessment is running durably on the server. You can safely navigate away or refresh at any time; progress will be preserved.
          </p>
        </div>
      ) : null}

      {/* Simulation Impact Panel */}
      {impact ? (
        <div className="flex flex-col gap-4">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-base font-semibold">Simulated impact on source run</h2>
              <p className="text-xs text-muted-foreground">
                Strict deterministic replay under current engine · Validation coverage: <strong className="font-semibold">{impact.coverage}</strong>
              </p>
            </div>
            {impact.preservedMatches ? (
              <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded bg-green-500/10 text-green-700 dark:text-green-400 border border-green-500/30 text-xs font-medium">
                <ShieldCheck className="size-4" /> Match Preservation Verified (0 Regressions)
              </span>
            ) : (
              <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded bg-red-500/10 text-red-700 dark:text-red-400 border border-red-500/30 text-xs font-medium">
                <ShieldAlert className="size-4" /> Match Regressions Detected ({impact.lostGroupsCount} lost, {impact.degradedGroupsCount} degraded)
              </span>
            )}
          </div>

          <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
            <div className="border bg-card rounded-lg p-4 flex flex-col">
              <span className="text-xs text-muted-foreground font-medium">Full Match Rate</span>
              <div className="flex items-baseline gap-2 mt-1">
                <span className="text-2xl font-bold tabular-nums">
                  {impact.candidateFullMatchPercentage.toFixed(1)}%
                </span>
                <span
                  className={`text-xs font-semibold px-1.5 py-0.5 rounded ${
                    impact.fullImprovementPercentagePoints > 0
                      ? "bg-green-500/10 text-green-700 dark:text-green-400"
                      : "bg-muted text-muted-foreground"
                  }`}
                >
                  +{impact.fullImprovementPercentagePoints.toFixed(1)} pp
                </span>
              </div>
              <span className="text-[11px] text-muted-foreground mt-1">
                Base: {impact.baseFullMatchPercentage.toFixed(1)}% ({impact.candidateFullMatchRecords.toLocaleString()} recs)
              </span>
            </div>

            <div className="border bg-card rounded-lg p-4 flex flex-col">
              <span className="text-xs text-muted-foreground font-medium">Any Match Rate (Full+Partial)</span>
              <div className="flex items-baseline gap-2 mt-1">
                <span className="text-2xl font-bold tabular-nums">
                  {impact.candidateAnyMatchPercentage.toFixed(1)}%
                </span>
                <span
                  className={`text-xs font-semibold px-1.5 py-0.5 rounded ${
                    impact.anyImprovementPercentagePoints > 0
                      ? "bg-green-500/10 text-green-700 dark:text-green-400"
                      : "bg-muted text-muted-foreground"
                  }`}
                >
                  +{impact.anyImprovementPercentagePoints.toFixed(1)} pp
                </span>
              </div>
              <span className="text-[11px] text-muted-foreground mt-1">
                Base: {impact.baseAnyMatchPercentage.toFixed(1)}%
              </span>
            </div>

            <div className="border bg-card rounded-lg p-4 flex flex-col">
              <span className="text-xs text-muted-foreground font-medium">Recovered Unresolved</span>
              <div className="flex items-baseline gap-2 mt-1">
                <span className="text-2xl font-bold tabular-nums text-primary">
                  {impact.recoveredUnresolvedPercentage.toFixed(1)}%
                </span>
              </div>
              <span className="text-[11px] text-muted-foreground mt-1">
                {impact.recoveredUnresolvedRecords.toLocaleString()} of {impact.baseUnresolvedRecords.toLocaleString()} originally unresolved
              </span>
            </div>

            <div className="border bg-card rounded-lg p-4 flex flex-col">
              <span className="text-xs text-muted-foreground font-medium">Protected Baseline Matches</span>
              <div className="flex items-baseline gap-2 mt-1">
                <span className="text-2xl font-bold tabular-nums text-green-700 dark:text-green-400">
                  {impact.protectedMatchedRecords.toLocaleString()}
                </span>
                <span className="text-xs text-muted-foreground font-normal">recs</span>
              </div>
              <span className="text-[11px] text-muted-foreground mt-1">
                {impact.preservedGroupsCount} match groups preserved unchanged
              </span>
            </div>
          </div>

          {/* Dataset Breakdown */}
          {impact.datasetBreakdowns?.length ? (
            <div className="border rounded-lg overflow-hidden bg-card text-xs">
              <div className="bg-muted/40 px-3 py-2 font-medium border-b flex items-center justify-between">
                <span>Per-Dataset Impact Accounting</span>
                <span className="text-muted-foreground font-normal">{impact.datasetBreakdowns.length} datasets participating</span>
              </div>
              <table className="w-full text-left">
                <thead className="border-b bg-muted/20 text-muted-foreground">
                  <tr>
                    <th className="px-3 py-2 font-medium">Dataset</th>
                    <th className="px-3 py-2 font-medium text-right">Total</th>
                    <th className="px-3 py-2 font-medium text-right">Base Full</th>
                    <th className="px-3 py-2 font-medium text-right">Base Unres</th>
                    <th className="px-3 py-2 font-medium text-right">Candidate Full</th>
                    <th className="px-3 py-2 font-medium text-right">Candidate Unres</th>
                    <th className="px-3 py-2 font-medium text-right">Net Full Gain</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {impact.datasetBreakdowns.map((b) => {
                    const diff = b.candidateFullMatchRecords - b.baseFullMatchRecords
                    return (
                      <tr key={b.datasetCode} className="hover:bg-muted/20">
                        <td className="px-3 py-2 font-mono font-medium">{b.datasetCode}</td>
                        <td className="px-3 py-2 text-right tabular-nums">{b.totalRecords.toLocaleString()}</td>
                        <td className="px-3 py-2 text-right tabular-nums">{b.baseFullMatchRecords.toLocaleString()}</td>
                        <td className="px-3 py-2 text-right tabular-nums text-amber-700 dark:text-amber-400">
                          {b.baseUnresolvedRecords.toLocaleString()}
                        </td>
                        <td className="px-3 py-2 text-right tabular-nums font-semibold">{b.candidateFullMatchRecords.toLocaleString()}</td>
                        <td className="px-3 py-2 text-right tabular-nums">{b.candidateUnresolvedRecords.toLocaleString()}</td>
                        <td className="px-3 py-2 text-right tabular-nums font-semibold text-green-700 dark:text-green-400">
                          {diff >= 0 ? `+${diff}` : diff}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          ) : null}

          {/* Regression Checks Details */}
          {impact.regressionVerdicts?.length ? (
            <div className="border rounded-lg p-3 bg-card text-xs flex flex-col gap-2">
              <span className="font-semibold text-muted-foreground uppercase tracking-wider text-[11px]">
                Regression Verification Checks ({impact.regressionVerdicts.length})
              </span>
              <div className="grid gap-2 sm:grid-cols-2 md:grid-cols-3">
                {impact.regressionVerdicts.map((v, i) => (
                  <div
                    key={i}
                    className={`border rounded p-2.5 flex items-start gap-2 ${
                      v.passed ? "bg-green-500/5 border-green-500/20" : "bg-red-500/5 border-red-500/20"
                    }`}
                  >
                    {v.passed ? (
                      <CheckCircle className="size-4 text-green-600 shrink-0 mt-0.5" />
                    ) : (
                      <XCircle className="size-4 text-red-600 shrink-0 mt-0.5" />
                    )}
                    <div>
                      <p className="font-medium">{v.description}</p>
                      <p className="text-[11px] text-muted-foreground">{v.reason}</p>
                      <p className="text-[10px] text-muted-foreground mt-0.5 tabular-nums">
                        {v.matchedRecords} matched of {v.totalRecords} records
                      </p>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          ) : null}
        </div>
      ) : null}

      {/* Diagnosis Section */}
      {analysis.diagnoses?.length ? (
        <div className="flex flex-col gap-3">
          <h2 className="text-base font-semibold">Diagnosis of Unresolved Records</h2>
          <div className="grid gap-3 sm:grid-cols-2">
            {analysis.diagnoses.map((diag, idx) => (
              <div key={idx} className="border bg-card rounded-lg p-3.5 flex flex-col gap-1.5 text-xs">
                <div className="flex items-center justify-between gap-2">
                  <span className="font-semibold text-foreground uppercase tracking-wide text-[11px]">
                    {diag.category.replace(/_/g, " ")}
                  </span>
                  <span className="px-2 py-0.5 rounded-full bg-muted font-mono font-medium text-[11px] tabular-nums">
                    {diag.affectedRecordCount} records affected
                  </span>
                </div>
                <p className="text-muted-foreground">{diag.summary}</p>
                {diag.sampleEvidenceIds?.length ? (
                  <div className="flex flex-wrap gap-1 mt-1">
                    {diag.sampleEvidenceIds.map((id) => (
                      <span key={id} className="font-mono text-[10px] bg-muted/60 border rounded px-1.5 py-0.5">
                        {id}
                      </span>
                    ))}
                  </div>
                ) : null}
              </div>
            ))}
          </div>
        </div>
      ) : null}

      {/* Proposed Changes Section */}
      <div className="flex flex-col gap-4">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-base font-semibold">Proposed Configuration Changes</h2>
            <p className="text-xs text-muted-foreground">
              Grouped by dependency. Select groups to revalidate or accept.
            </p>
          </div>
          {isAwaitingDecision && groupedChanges.length > 0 ? (
            <div className="flex items-center gap-2 text-xs">
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  const allSelectable = groupedChanges.filter((g) => g.isSelectable).map((g) => g.groupId)
                  selectionMutation.mutate(allSelectable)
                }}
              >
                Select All
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => selectionMutation.mutate([])}
              >
                Deselect All
              </Button>
            </div>
          ) : null}
        </div>

        {groupedChanges.length === 0 ? (
          <div className="border bg-card rounded-lg p-8 text-center text-xs text-muted-foreground">
            No configuration changes proposed.
          </div>
        ) : (
          <div className="flex flex-col gap-4">
            {groupedChanges.map((group) => {
              const isSelected = selectedGroupIds.includes(group.groupId)
              const contribution = impact?.groupContributions?.find((c) => c.groupId === group.groupId)

              return (
                <div
                  key={group.groupId}
                  className={`border rounded-lg bg-card transition-all ${
                    isSelected ? "border-primary ring-1 ring-primary/20" : "opacity-90"
                  }`}
                >
                  {/* Group Header */}
                  <div className="p-4 flex flex-wrap items-center justify-between gap-3 bg-muted/20 border-b rounded-t-lg">
                    <div className="flex items-center gap-3">
                      {isAwaitingDecision ? (
                        <input
                          type="checkbox"
                          className="size-4 rounded border-gray-300 text-primary focus:ring-primary"
                          checked={isSelected}
                          disabled={!group.isSelectable || selectionMutation.isPending}
                          onChange={() => toggleGroup(group.groupId)}
                        />
                      ) : null}
                      <div>
                        <div className="flex items-center gap-2">
                          <span className="font-semibold text-sm">Dependency Group: {group.groupId}</span>
                          {group.hasMaterialChange ? (
                            <span className="px-2 py-0.5 rounded text-[10px] font-medium bg-amber-500/10 text-amber-700 border border-amber-500/30">
                              Material Change
                            </span>
                          ) : (
                            <span className="px-2 py-0.5 rounded text-[10px] font-medium bg-green-500/10 text-green-700 border border-green-500/30">
                              Safe Additive
                            </span>
                          )}
                          {!group.isSelectable ? (
                            <span className="px-2 py-0.5 rounded text-[10px] font-medium bg-red-500/10 text-red-700 border border-red-500/30">
                              Blocked
                            </span>
                          ) : null}
                        </div>
                        {group.blockerReason ? (
                          <p className="text-xs text-red-600 mt-0.5">{group.blockerReason}</p>
                        ) : null}
                      </div>
                    </div>

                    {/* Group Contribution Metrics */}
                    {contribution ? (
                      <div className="flex items-center gap-3 text-xs">
                        <span className="text-muted-foreground">
                          Independent Gain:{" "}
                          <strong className="text-foreground">+{contribution.independentAnyGainPoints.toFixed(1)} pp</strong>
                        </span>
                        <span className="text-muted-foreground">
                          Marginal Gain:{" "}
                          <strong className="text-foreground">+{contribution.marginalAnyGainPoints.toFixed(1)} pp</strong>
                        </span>
                        <span className="px-2 py-0.5 rounded bg-muted font-mono text-[11px]">
                          +{contribution.newlyMatchedRecords} records
                        </span>
                      </div>
                    ) : null}
                  </div>

                  {/* Group Items */}
                  <div className="p-4 flex flex-col gap-3">
                    {group.items.map((change) => {
                      const isExpanded = Boolean(expandedDiffs[change.id])
                      return (
                        <div key={change.id} className="border rounded-md p-3.5 bg-background flex flex-col gap-2 text-xs">
                          <div className="flex flex-wrap items-center justify-between gap-2">
                            <div className="flex items-center gap-2">
                              <span className="font-semibold uppercase tracking-wider text-[11px] px-2 py-0.5 rounded bg-muted">
                                {change.changeType}
                              </span>
                              <span className="font-mono font-medium">{change.operationType}</span>
                              <span className="text-muted-foreground">→</span>
                              <span className="font-mono text-muted-foreground">{change.targetIdentifier}</span>
                            </div>
                            <Button
                              variant="ghost"
                              size="sm"
                              className="h-7 text-xs flex items-center gap-1"
                              onClick={() => toggleDiff(change.id)}
                            >
                              <FileDiff className="size-3.5" />
                              {isExpanded ? "Hide diff" : "Show diff"}
                              {isExpanded ? <ChevronDown className="size-3" /> : <ChevronRight className="size-3" />}
                            </Button>
                          </div>

                          <p className="text-foreground/90">{change.rationale}</p>

                          {change.evidenceIds?.length ? (
                            <div className="flex flex-wrap items-center gap-1 text-[11px] text-muted-foreground">
                              <span>Evidence:</span>
                              {change.evidenceIds.map((eid) => (
                                <span key={eid} className="font-mono bg-muted px-1.5 py-0.2 rounded text-[10px]">
                                  {eid}
                                </span>
                              ))}
                            </div>
                          ) : null}

                          {/* Diff Box */}
                          {isExpanded ? (
                            <div className="mt-2 grid sm:grid-cols-2 gap-2 font-mono text-[11px] border rounded p-2 bg-muted/20">
                              <div>
                                <span className="text-muted-foreground font-sans font-medium text-[10px] uppercase">
                                  Before
                                </span>
                                <pre className="mt-1 p-2 bg-background border rounded overflow-x-auto whitespace-pre-wrap max-h-48 text-[10px]">
                                  {(() => {
                                    try {
                                      return JSON.stringify(JSON.parse(change.beforeJson), null, 2)
                                    } catch {
                                      return change.beforeJson
                                    }
                                  })()}
                                </pre>
                              </div>
                              <div>
                                <span className="text-muted-foreground font-sans font-medium text-[10px] uppercase">
                                  After (Proposed)
                                </span>
                                <pre className="mt-1 p-2 bg-background border rounded overflow-x-auto whitespace-pre-wrap max-h-48 text-[10px] text-green-700 dark:text-green-400">
                                  {(() => {
                                    try {
                                      return JSON.stringify(JSON.parse(change.afterJson), null, 2)
                                    } catch {
                                      return change.afterJson
                                    }
                                  })()}
                                </pre>
                              </div>
                            </div>
                          ) : null}
                        </div>
                      )
                    })}
                  </div>
                </div>
              )
            })}
          </div>
        )}
      </div>

      {/* Decision Action Card */}
      {isAwaitingDecision ? (
        <div className="border bg-card rounded-lg p-5 flex flex-col gap-4 sticky bottom-4 shadow-lg border-primary/30">
          {hasSelectedMaterial && !showMaterialConfirm ? (
            <div className="bg-amber-500/10 border border-amber-500/30 rounded p-3 text-xs text-amber-800 dark:text-amber-300 flex items-start gap-2">
              <AlertTriangle className="size-4 shrink-0 mt-0.5" />
              <div>
                <p className="font-semibold">Selected changes include material modifications</p>
                <p className="mt-0.5 text-[11px]">
                  One or more selected changes modify existing rules, tolerances, or mappings. These changes require explicit acknowledgement before committing.
                </p>
              </div>
            </div>
          ) : null}

          {showMaterialConfirm ? (
            <div className="bg-amber-500/10 border border-amber-500/30 rounded p-4 text-xs flex flex-col gap-2">
              <p className="font-semibold text-amber-900 dark:text-amber-200">
                Confirm Material Modification Acceptance
              </p>
              <p className="text-muted-foreground">
                By accepting, you confirm that zero regressions were detected across the regression suite and that the material configuration change is necessary.
              </p>
            </div>
          ) : null}

          <div className="flex flex-wrap items-center justify-between gap-4">
            <div className="text-xs text-muted-foreground">
              <span>{selectedGroupIds.length} dependency group(s) selected</span>
              {impact ? (
                <span>
                  {" "}· Simulated Any Match: <strong className="text-foreground">{impact.candidateAnyMatchPercentage.toFixed(1)}%</strong>
                </span>
              ) : null}
            </div>

            <div className="flex items-center gap-3">
              <Button
                variant="outline"
                size="sm"
                disabled={decisionMutation.isPending}
                onClick={() => {
                  if (window.confirm("Reject all proposed changes? No configuration changes will be committed.")) {
                    decisionMutation.mutate("RejectAll")
                  }
                }}
              >
                Reject All
              </Button>

              {hasSelectedMaterial && !showMaterialConfirm ? (
                <Button
                  size="sm"
                  disabled={!analysis.canAccept || selectedGroupIds.length === 0 || decisionMutation.isPending}
                  onClick={() => setShowMaterialConfirm(true)}
                >
                  <ShieldCheck className="size-4" /> Review Material Acknowledgement
                </Button>
              ) : (
                <Button
                  size="sm"
                  disabled={!analysis.canAccept || selectedGroupIds.length === 0 || decisionMutation.isPending}
                  onClick={() => decisionMutation.mutate("AcceptSelected")}
                >
                  {decisionMutation.isPending ? (
                    <Loader2 className="size-4 animate-spin" />
                  ) : (
                    <CheckCircle className="size-4" />
                  )}
                  Accept Selected Changes
                </Button>
              )}
            </div>
          </div>
        </div>
      ) : null}

      {/* Post-Acceptance Controls: Run Now / Run Later */}
      {isAccepted ? (
        <div className="border bg-green-500/5 border-green-500/30 rounded-lg p-5 flex flex-col gap-4">
          <div className="flex items-start justify-between gap-4">
            <div className="flex items-start gap-3">
              <ShieldCheck className="size-6 text-green-600 shrink-0 mt-0.5" />
              <div>
                <h3 className="text-base font-semibold text-foreground">
                  Configuration Committed: Profile v{analysis.committedProfileVersion}
                </h3>
                <p className="text-xs text-muted-foreground mt-0.5">
                  The accepted changes have been atomically persisted as an approved profile version. You can execute reconciliation now on the assessed source inputs, or run later.
                </p>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <Button
                variant="outline"
                size="sm"
                onClick={() => navigate("/configuration/manual/profiles")}
              >
                Run Later
              </Button>
              <Button
                size="sm"
                disabled={executeMutation.isPending || Boolean(executionQuery.data && ["Queued", "PreparingInputs", "Reconciling"].includes(executionQuery.data.status))}
                onClick={() => executeMutation.mutate()}
              >
                {executeMutation.isPending || (executionQuery.data && ["Queued", "PreparingInputs", "Reconciling"].includes(executionQuery.data.status)) ? (
                  <Loader2 className="size-4 animate-spin" />
                ) : (
                  <Play className="size-4" />
                )}
                Run Now (Assessed Inputs)
              </Button>
            </div>
          </div>

          {/* Execution Progress & Result */}
          {executionQuery.data ? (
            <div className="border rounded bg-card p-4 text-xs flex flex-col gap-2">
              <div className="flex items-center justify-between">
                <span className="font-semibold text-muted-foreground uppercase tracking-wider text-[11px]">
                  Execution Status
                </span>
                <span className="font-mono font-medium">{executionQuery.data.status} ({executionQuery.data.stage})</span>
              </div>
              {executionQuery.data.errorMessage ? (
                <p className="text-red-600">{executionQuery.data.errorMessage}</p>
              ) : null}
              {executionQuery.data.resultingRunId ? (
                <div className="flex items-center justify-between pt-2 border-t">
                  <span>Reconciliation completed successfully.</span>
                  <Button
                    size="sm"
                    variant="outline"
                    render={<Link to={`/reconciliation/runs/${executionQuery.data.resultingRunId}`} />}
                  >
                    View Reconciliation Run →
                  </Button>
                </div>
              ) : null}
            </div>
          ) : null}
        </div>
      ) : null}

      {/* Rejected / NoRecs info */}
      {isRejected ? (
        <div className="border bg-card rounded-lg p-5 flex items-center justify-between">
          <div className="flex items-center gap-3 text-xs">
            <XCircle className="size-5 text-red-500" />
            <div>
              <p className="font-medium text-foreground">Assessment Rejected</p>
              <p className="text-muted-foreground">All proposed changes were rejected. The profile remains on version {analysis.profileVersion}.</p>
            </div>
          </div>
          <Button
            variant="outline"
            size="sm"
            render={<Link to={`/reconciliation/profiles/${profileId}`} />}
          >
            Back to profile
          </Button>
        </div>
      ) : null}

      {isNoRecs ? (
        <div className="border bg-card rounded-lg p-5 flex items-center justify-between">
          <div className="flex items-center gap-3 text-xs">
            <AlertTriangle className="size-5 text-amber-500" />
            <div>
              <p className="font-medium text-foreground">No Safe Recommendations</p>
              <p className="text-muted-foreground">The AI advisor found no additive rules or configuration adjustments that could safely recover unresolved records without risking regressions.</p>
            </div>
          </div>
          <Button
            variant="outline"
            size="sm"
            render={<Link to={`/reconciliation/profiles/${profileId}`} />}
          >
            Back to profile
          </Button>
        </div>
      ) : null}
    </div>
  )
}
