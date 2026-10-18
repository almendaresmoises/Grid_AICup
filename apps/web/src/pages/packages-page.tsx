import { Button } from "@workspace/ui/components/button"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { AlertTriangle, ChevronDown, ChevronRight, Database, Eye, Loader2, Pencil, Plus, Search, ShieldCheck, Sparkles, Trash2 } from "lucide-react"
import { Fragment, useMemo, useState } from "react"
import { Link, useNavigate } from "react-router"
import { ErrorNote } from "@/components/error-note"
import { Modal } from "@/components/modal"
import { OriginCell } from "@/components/origin-cell"
import { api } from "@/lib/api"
import {
  getProfileStatuses,
  reconciliationAnalysisKeys,
  startOrResumeAnalysis,
  type ProfileAnalysisSummary,
} from "@/lib/reconciliation-analysis"
import { parseProfile, type Package, type Profile } from "@/lib/types"
import { inputClass, labelClass, textareaClass } from "@/lib/ui"

export function PackagesPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [search, setSearch] = useState("")
  const [creating, setCreating] = useState(false)
  const [name, setName] = useState("")
  const [description, setDescription] = useState("")
  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [drilldownItem, setDrilldownItem] = useState<{ package: Package; profile?: Profile } | null>(null)
  const [analyzeTarget, setAnalyzeTarget] = useState<{ package: Package; summary?: ProfileAnalysisSummary } | null>(null)
  const [analyzeContext, setAnalyzeContext] = useState("")

  const packages = useQuery({
    queryKey: ["packages"],
    queryFn: () => api<Package[]>("/api/app/package"),
  })
  const profiles = useQuery({
    queryKey: ["profile-registry", packages.data?.map((item) => item.profileId).join("|")],
    enabled: Boolean(packages.data?.length),
    queryFn: () => Promise.all((packages.data ?? []).map((item) => api<Profile>(`/api/app/profile/${item.profileId}`))),
  })

  const profileIds = useMemo(() => (packages.data ?? []).map((p) => p.profileId), [packages.data])
  const profileStatuses = useQuery({
    queryKey: reconciliationAnalysisKeys.profileStatuses(profileIds),
    enabled: profileIds.length > 0,
    queryFn: () => getProfileStatuses(profileIds),
  })
  const statusMap = useMemo(
    () => new Map(profileStatuses.data?.map((s) => [s.profileId, s]) ?? []),
    [profileStatuses.data]
  )

  const startAnalysisMutation = useMutation({
    mutationFn: async ({ profileId, sourceRunId, context }: { profileId: string; sourceRunId?: string; context?: string }) => {
      return startOrResumeAnalysis({
        requestId: crypto.randomUUID(),
        profileId,
        sourceRunId,
        context,
      })
    },
    onSuccess: (result, variables) => {
      setAnalyzeTarget(null)
      setAnalyzeContext("")
      navigate(`/reconciliation/profiles/${variables.profileId}/analyses/${result.analysisId}`)
    },
  })

  const handleAnalyzeClick = (pkg: Package, summary?: ProfileAnalysisSummary) => {
    if (summary?.activeAnalysisId) {
      navigate(`/reconciliation/profiles/${pkg.profileId}/analyses/${summary.activeAnalysisId}`)
      return
    }
    if (summary?.lastAnalysisId && summary.actionType !== "start") {
      navigate(`/reconciliation/profiles/${pkg.profileId}/analyses/${summary.lastAnalysisId}`)
      return
    }
    setAnalyzeTarget({ package: pkg, summary })
    setAnalyzeContext("")
  }

  const create = useMutation({
    mutationFn: () => api<Package>("/api/app/package", { method: "POST", body: JSON.stringify({ name, description }) }),
    onSuccess: async (created) => {
      setName("")
      setDescription("")
      setCreating(false)
      await queryClient.invalidateQueries({ queryKey: ["packages"] })
      navigate(`/reconciliation/profiles/${created.profileId}`)
    },
  })
  const remove = useMutation({
    mutationFn: (packageId: string) =>
      api(`/api/app/package/${packageId}`, { method: "DELETE" }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["packages"] })
      await queryClient.invalidateQueries({ queryKey: ["profile-registry"] })
    },
  })

  const rows = useMemo(() => {
    const needle = search.trim().toLowerCase()
    return (packages.data ?? [])
      .map((item) => ({ package: item, profile: profiles.data?.find((profile) => profile.id === item.profileId) }))
      .filter(({ package: item, profile }) => {
        if (!needle) return true
        const spec = profile ? parseProfile(profile.definitionJson) : null
        const origin = item.createdWithAi || profile?.createdWithAi ? "ai" : "manual"
        return [item.name, item.description, item.profileStatus, origin, ...(spec?.datasets ?? []), ...(spec?.rules.map((rule) => rule.id) ?? [])]
          .join(" ")
          .toLowerCase()
          .includes(needle)
      })
  }, [packages.data, profiles.data, search])

  const ruleCount = (profiles.data ?? []).reduce((total, profile) => total + parseProfile(profile.definitionJson).rules.length, 0)
  const datasetCount = (profiles.data ?? []).reduce((total, profile) => total + parseProfile(profile.definitionJson).datasets.length, 0)

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between gap-4">
        <p className="text-muted-foreground text-sm">Matching profiles, the datasets they use, and the rule passes that run in order.</p>
        <Button size="sm" type="button" onClick={() => setCreating(true)}>
          <Plus />
          Create profile
        </Button>
      </div>
      <div className="grid gap-3 sm:grid-cols-3">
        <Stat label="Profiles" value={packages.data?.length ?? 0} />
        <Stat label="Rule passes" value={ruleCount} />
        <Stat label="Mapped datasets" value={datasetCount} />
      </div>
      {packages.error ? <ErrorNote message={(packages.error as Error).message} /> : null}
      {remove.error ? <ErrorNote message={(remove.error as Error).message} /> : null}
      <Modal
        isOpen={creating}
        onClose={() => setCreating(false)}
        title="Create reconciliation profile"
        description="Name your profile and describe its matching scope."
        size="lg"
        footer={
          <div className="flex items-center justify-end gap-2">
            <Button type="button" variant="outline" size="sm" onClick={() => setCreating(false)}>
              Cancel
            </Button>
            <Button
              type="submit"
              form="create-profile-form"
              size="sm"
              disabled={create.isPending || !name.trim()}
            >
              {create.isPending ? "Creating…" : "Save profile"}
            </Button>
          </div>
        }
      >
        <form
          id="create-profile-form"
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault()
            create.mutate()
          }}
        >
          {create.error ? <ErrorNote message={(create.error as Error).message} /> : null}
          <div className="grid gap-4 sm:grid-cols-2">
            <label className="flex flex-col gap-1.5">
              <span className={labelClass}>Name</span>
              <input
                className={inputClass}
                value={name}
                onChange={(event) => setName(event.target.value)}
                placeholder="e.g. Daily Settlement"
                required
              />
            </label>
            <label className="flex flex-col gap-1.5">
              <span className={labelClass}>Description</span>
              <input
                className={inputClass}
                value={description}
                onChange={(event) => setDescription(event.target.value)}
                placeholder="Optional description"
              />
            </label>
          </div>
        </form>
      </Modal>
      <div className="overflow-hidden border bg-card">
        <div className="flex items-center gap-3 border-b px-3 py-3">
          <div className="relative min-w-0 flex-1">
            <Search className="text-muted-foreground absolute top-1/2 left-3 size-4 -translate-y-1/2" />
            <input
              className="border-input h-9 w-full border bg-background pr-3 pl-9 text-sm outline-none"
              value={search}
              placeholder="Search profile, dataset, or rule"
              onChange={(event) => setSearch(event.target.value)}
            />
          </div>
          <span className="text-muted-foreground text-xs tabular-nums">{rows.length} shown</span>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-muted-foreground bg-muted/40 border-b text-left text-xs">
                <th className="w-10 px-3 py-2.5"><span className="sr-only">Expand</span></th>
                <th className="px-3 py-2.5 font-medium">Profile</th>
                <th className="px-3 py-2.5 font-medium">Datasets</th>
                <th className="px-3 py-2.5 font-medium">Rules</th>
                <th className="px-3 py-2.5 font-medium">Status</th>
                <th className="px-3 py-2.5 font-medium">Optimization</th>
                <th className="px-3 py-2.5 font-medium">Origin</th>
                <th className="px-3 py-2.5 text-right font-medium"><span className="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 ? (
                <tr>
                  <td colSpan={8} className="text-muted-foreground px-4 py-10 text-center">
                    <ShieldCheck className="mx-auto mb-2 size-6 opacity-40" />
                    {search ? "No profiles match the current search." : "No reconciliation profiles yet."}
                  </td>
                </tr>
              ) : null}
              {rows.map(({ package: item, profile }) => {
                const spec = profile ? parseProfile(profile.definitionJson) : null
                const expanded = expandedId === item.id
                return (
                  <Fragment key={item.id}>
                    <tr className="hover:bg-muted/30 cursor-pointer border-b" onClick={() => setExpandedId(expanded ? null : item.id)}>
                      <td className="px-3 py-3">{expanded ? <ChevronDown className="size-4" /> : <ChevronRight className="size-4" />}</td>
                      <td className="px-3 py-3">
                        <p className="font-medium">{item.name}</p>
                        <p className="text-muted-foreground text-xs">Version {item.profileVersion}{item.description ? ` · ${item.description}` : ""}</p>
                      </td>
                      <td className="px-3 py-3 tabular-nums">{spec?.datasets.length ?? "—"}</td>
                      <td className="px-3 py-3 tabular-nums">{spec?.rules.length ?? "—"}</td>
                      <td className="px-3 py-3">
                        <span className="inline-flex items-center gap-2">
                          <span className={`size-1.5 ${item.profileStatus === "Approved" ? "bg-primary" : "bg-foreground/30"}`} />
                          {item.profileStatus}
                        </span>
                      </td>
                      <td className="px-3 py-3" onClick={(event) => event.stopPropagation()}>
                        {(() => {
                          const summary = statusMap.get(item.profileId)
                          if (!summary) return <span className="text-muted-foreground text-xs">—</span>
                          return (
                            <div className="flex items-center gap-1.5">
                              <Button
                                variant="outline"
                                size="sm"
                                className="h-7 text-xs px-2.5 gap-1.5 font-normal"
                                onClick={() => handleAnalyzeClick(item, summary)}
                              >
                                <Sparkles className="size-3 text-primary shrink-0" />
                                <span>{summary.actionLabel}</span>
                              </Button>
                              {summary.activeStatus ? (
                                <span className="text-[10px] text-muted-foreground font-mono">
                                  ({summary.activeStatus})
                                </span>
                              ) : null}
                            </div>
                          )
                        })()}
                      </td>
                      <td className="px-3 py-3"><OriginCell createdWithAi={item.createdWithAi || profile?.createdWithAi} /></td>
                      <td className="px-3 py-3 text-right" onClick={(event) => event.stopPropagation()}>
                        <div className="flex items-center justify-end gap-1">
                          <Button
                            variant="ghost"
                            size="icon-sm"
                            aria-label={`View ${item.name} details`}
                            title="Drilldown details"
                            onClick={() => setDrilldownItem({ package: item, profile })}
                          >
                            <Eye />
                          </Button>
                          <Button variant="ghost" size="icon-sm" aria-label={`Edit ${item.name}`} title="Edit profile" render={<Link to={`/reconciliation/profiles/${item.profileId}`} />}>
                            <Pencil />
                          </Button>
                          <Button
                            variant="ghost"
                            size="icon-sm"
                            aria-label={`Delete ${item.name}`}
                            title="Delete profile"
                            disabled={remove.isPending}
                            onClick={() => {
                              if (window.confirm(`Delete reconciliation profile "${item.name}"? All associated runs, baselines, and match groups will be removed.`)) {
                                remove.mutate(item.id)
                              }
                            }}
                          >
                            <Trash2 />
                          </Button>
                        </div>
                      </td>
                    </tr>
                    {expanded ? (
                      <tr className="border-b bg-muted/20">
                        <td colSpan={8} className="px-4 py-3">
                          <div className="grid gap-4 sm:grid-cols-2">
                            <div>
                              <p className="text-muted-foreground mb-1 flex items-center gap-1 text-xs"><Database className="size-3" /> Datasets</p>
                              <p className="text-sm">{spec?.datasets.length ? spec.datasets.join(", ") : "None mapped"}</p>
                            </div>
                            <div>
                              <p className="text-muted-foreground mb-1 text-xs">Rule passes</p>
                              <p className="text-sm">{spec?.rules.length ? spec.rules.map((rule) => `${rule.id} (${rule.type})`).join(", ") : "None"}</p>
                            </div>
                          </div>
                        </td>
                      </tr>
                    ) : null}
                  </Fragment>
                )
              })}
            </tbody>
          </table>
        </div>
      </div>

      {/* Profile Drilldown Modal */}
      <Modal
        isOpen={Boolean(drilldownItem)}
        onClose={() => setDrilldownItem(null)}
        title={drilldownItem ? `${drilldownItem.package.name} · Profile Drilldown` : ""}
        description={
          drilldownItem ? (
            <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <span>Version {drilldownItem.package.profileVersion}</span>
              <span>· Status: <strong className="text-foreground">{drilldownItem.package.profileStatus}</strong></span>
              {drilldownItem.package.description ? <span>· {drilldownItem.package.description}</span> : null}
            </span>
          ) : undefined
        }
        size="3xl"
        footer={
          <div className="flex items-center justify-between w-full">
            <span className="text-xs text-muted-foreground">
              Created {drilldownItem?.package.creationTime ? new Date(drilldownItem.package.creationTime).toLocaleString() : ""}
            </span>
            <div className="flex items-center gap-2">
              <Button variant="outline" size="sm" onClick={() => setDrilldownItem(null)}>
                Close
              </Button>
              {drilldownItem ? (
                <Button
                  size="sm"
                  render={<Link to={`/reconciliation/profiles/${drilldownItem.package.profileId}`} />}
                >
                  <Pencil className="size-3.5" />
                  Edit in Studio
                </Button>
              ) : null}
            </div>
          </div>
        }
      >
        {drilldownItem ? (
          (() => {
            const spec = drilldownItem.profile ? parseProfile(drilldownItem.profile.definitionJson) : null
            return (
              <div className="flex flex-col gap-5">
                <div>
                  <h3 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground mb-2 flex items-center gap-1.5">
                    <Database className="size-3.5" /> Participating Datasets ({spec?.datasets.length ?? 0})
                  </h3>
                  <div className="flex flex-wrap gap-2">
                    {spec?.datasets.length ? (
                      spec.datasets.map((code) => (
                        <span key={code} className="rounded border bg-muted/40 px-2.5 py-1 font-mono text-xs font-medium text-foreground">
                          {code}
                        </span>
                      ))
                    ) : (
                      <span className="text-xs text-muted-foreground">No datasets mapped yet.</span>
                    )}
                  </div>
                </div>

                <div>
                  <h3 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground mb-2">
                    Rule Passes ({spec?.rules.length ?? 0})
                  </h3>
                  {spec?.rules.length ? (
                    <div className="overflow-x-auto border">
                      <table className="w-full text-xs">
                        <thead>
                          <tr className="border-b bg-muted/40 text-left text-muted-foreground">
                            <th className="px-3 py-2 font-medium">Pass</th>
                            <th className="px-3 py-2 font-medium">Type</th>
                            <th className="px-3 py-2 font-medium">Datasets</th>
                            <th className="px-3 py-2 font-medium">Keys / Tolerances</th>
                          </tr>
                        </thead>
                        <tbody>
                          {spec.rules.map((rule, idx) => (
                            <tr key={idx} className="border-b last:border-0 hover:bg-muted/20">
                              <td className="px-3 py-2 font-mono font-medium">{rule.id}</td>
                              <td className="px-3 py-2">
                                <span className="rounded bg-primary/10 px-1.5 py-0.5 font-medium text-primary capitalize">
                                  {rule.type}
                                </span>
                              </td>
                              <td className="px-3 py-2 font-mono">{rule.datasets.join(", ")}</td>
                              <td className="px-3 py-2">
                                {rule.keys.map((k) => Object.entries(k.fields).map(([ds, col]) => `${ds}.${col}`).join(" = ")).join(" | ") || "—"}
                                {rule.min_score != null ? <span className="ml-2 text-amber-600 dark:text-amber-400 font-semibold">(Min score: {Math.round(rule.min_score * 100)}%)</span> : null}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  ) : (
                    <p className="text-xs text-muted-foreground">No rule passes defined yet.</p>
                  )}
                </div>
              </div>
            )
          })()
        ) : null}
      </Modal>

      {/* AI Optimization Scope Confirmation Modal */}
      <Modal
        isOpen={Boolean(analyzeTarget)}
        onClose={() => setAnalyzeTarget(null)}
        title="Optimize reconciliation with AI"
        description={analyzeTarget ? `Profile: ${analyzeTarget.package.name} (v${analyzeTarget.package.profileVersion})` : ""}
        size="lg"
        footer={
          <div className="flex items-center justify-end gap-2">
            <Button variant="outline" size="sm" onClick={() => setAnalyzeTarget(null)}>
              Cancel
            </Button>
            {analyzeTarget?.summary?.actionType === "no_run" ? (
              <Button
                size="sm"
                render={<Link to="/reconciliation/runs/new" />}
              >
                Create run first
              </Button>
            ) : (
              <Button
                size="sm"
                disabled={startAnalysisMutation.isPending}
                onClick={() => {
                  if (analyzeTarget) {
                    startAnalysisMutation.mutate({
                      profileId: analyzeTarget.package.profileId,
                      sourceRunId: analyzeTarget.summary?.latestRunId,
                      context: analyzeContext,
                    })
                  }
                }}
              >
                {startAnalysisMutation.isPending ? (
                  <Loader2 className="size-3.5 animate-spin" />
                ) : (
                  <Sparkles className="size-3.5" />
                )}
                Start optimization
              </Button>
            )}
          </div>
        }
      >
        {analyzeTarget?.summary?.actionType === "no_run" ? (
          <div className="flex flex-col gap-2 text-sm text-muted-foreground">
            <div className="flex items-center gap-2 text-amber-700 dark:text-amber-400">
              <AlertTriangle className="size-4 shrink-0" />
              <span className="font-medium">No successful run found</span>
            </div>
            <p>
              AI reconciliation optimization analyzes unmatched and ambiguous records from a completed run. Please create and run reconciliation for this profile before optimizing.
            </p>
          </div>
        ) : (
          <div className="flex flex-col gap-4 text-sm">
            <div className="border rounded bg-muted/20 p-3 text-xs flex flex-col gap-1">
              <span className="font-semibold text-foreground">Source input scope:</span>
              <p className="text-muted-foreground">
                {analyzeTarget?.summary?.latestRunId
                  ? `Default source run: #${analyzeTarget.summary.latestRunId.slice(0, 8)} (latest successful run)`
                  : "Latest successful run"}
              </p>
              {analyzeTarget?.summary?.explanation ? (
                <p className="text-muted-foreground italic">{analyzeTarget.summary.explanation}</p>
              ) : null}
            </div>

            <label className="flex flex-col gap-1.5">
              <span className={labelClass}>Optional business context</span>
              <textarea
                className={textareaClass}
                placeholder="Describe known exceptions, date/fee tolerances, or edge cases to guide the advisor..."
                rows={3}
                value={analyzeContext}
                onChange={(e) => setAnalyzeContext(e.target.value)}
              />
            </label>
            {startAnalysisMutation.error ? (
              <ErrorNote message={startAnalysisMutation.error.message} />
            ) : null}
          </div>
        )}
      </Modal>
    </div>
  )
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="border bg-card p-4">
      <p className="text-muted-foreground text-xs">{label}</p>
      <p className="mt-1 text-2xl font-medium tabular-nums">{value}</p>
    </div>
  )
}
