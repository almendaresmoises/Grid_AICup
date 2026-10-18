import { Button } from "@workspace/ui/components/button"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useEffect, useRef, useState } from "react"
import { Link, useNavigate, useSearchParams } from "react-router"
import {
  ArrowLeft,
  Check,
  Layers,
  Loader2,
  Play,
} from "lucide-react"
import { cn } from "cn"
import { ErrorNote } from "@/components/error-note"
import { ParserRunInput } from "@/components/parser-run-input"
import { api } from "@/lib/api"
import { uploadAndParse } from "@/lib/parser-runs"
import type { CanonicalDataset, ParserConfiguration } from "@/lib/canonical"
import { parseProfile, type Package, type Profile } from "@/lib/types"
import { inputClass } from "@/lib/ui"

export function CreateRunPage() {
  const [search] = useSearchParams()
  const [profileId, setProfileId] = useState(search.get("profileId") ?? "")
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  // State for dataset inputs
  const [stagedFiles, setStagedFiles] = useState<Record<string, File | null>>({})
  const [selectedRunIds, setSelectedRunIds] = useState<Record<string, string>>({})
  const [selectedParserIds, setSelectedParserIds] = useState<Record<string, string>>({})

  // State for pre-step upload/parse and reconciliation execution
  const [executionPhase, setExecutionPhase] = useState<"idle" | "parsing" | "reconciling">("idle")
  const [datasetStatuses, setDatasetStatuses] = useState<
    Record<string, "idle" | "uploading" | "parsing" | "succeeded" | "failed">
  >({})
  const [datasetErrors, setDatasetErrors] = useState<Record<string, string>>({})
  const [globalError, setGlobalError] = useState<string>("")

  const abortControllerRef = useRef<AbortController | null>(null)
  useEffect(() => {
    return () => {
      abortControllerRef.current?.abort()
    }
  }, [])

  const packages = useQuery({
    queryKey: ["packages"],
    queryFn: () => api<Package[]>("/api/app/package"),
  })

  const profile = useQuery({
    queryKey: ["profile", profileId],
    queryFn: () => api<Profile>(`/api/app/profile/${profileId}`),
    enabled: Boolean(profileId),
  })

  const datasets = useQuery({
    queryKey: ["canonical-datasets"],
    queryFn: () => api<CanonicalDataset[]>("/api/app/canonical-dataset"),
  })

  const parsers = useQuery({
    queryKey: ["parsers"],
    queryFn: () => api<ParserConfiguration[]>("/api/app/parser"),
  })

  const approvedPackages = (packages.data ?? []).filter(
    (item) => item.profileStatus === "Approved"
  )

  // Auto-select profile if only 1 approved profile exists and none was selected yet
  useEffect(() => {
    if (!profileId && approvedPackages.length === 1) {
      setProfileId(approvedPackages[0].profileId)
    }
  }, [profileId, approvedPackages])

  const selectedPackage = approvedPackages.find(
    (item) => item.profileId === profileId
  )

  const definition = profile.data?.approvedDefinitionJson
    ? parseProfile(profile.data.approvedDefinitionJson)
    : profile.data?.definitionJson
      ? parseProfile(profile.data.definitionJson)
      : null

  const codes = definition?.datasets ?? []

  function handleProfileChange(newProfileId: string) {
    setProfileId(newProfileId)
    setStagedFiles({})
    setSelectedRunIds({})
    setSelectedParserIds({})
    setDatasetStatuses({})
    setDatasetErrors({})
    setGlobalError("")
  }

  // Pre-step Upload & Parse and Run Reconciliation
  async function handleStartReconciliation() {
    if (executionPhase !== "idle") return
    setGlobalError("")
    setDatasetErrors({})

    const abortController = new AbortController()
    abortControllerRef.current = abortController
    const signal = abortController.signal

    try {
      // 1. Identify which datasets have staged files needing parsing
      const codesNeedingParse = codes.filter(
        (code) => stagedFiles[code] && !selectedRunIds[code]
      )
      const finalRunIds: Record<string, string> = { ...selectedRunIds }

      if (codesNeedingParse.length > 0) {
        setExecutionPhase("parsing")

        setDatasetStatuses((prev) => {
          const next = { ...prev }
          for (const code of codesNeedingParse) {
            next[code] = "parsing"
          }
          return next
        })

        // Execute parsing concurrently for all staged files
        const parseResults = await Promise.all(
          codesNeedingParse.map(async (code) => {
            const file = stagedFiles[code]!
            const parserId = selectedParserIds[code]
            if (!parserId) {
              throw new Error(`No parser selected for ${code}.`)
            }

            try {
              const run = await uploadAndParse(parserId, file, signal)
              setDatasetStatuses((prev) => ({ ...prev, [code]: "succeeded" }))
              void queryClient.invalidateQueries({
                queryKey: ["parser-runs", parserId],
              })
              return { code, runId: run.id }
            } catch (caught) {
              const message =
                caught instanceof Error ? caught.message : `Failed to parse ${file.name}`
              setDatasetStatuses((prev) => ({ ...prev, [code]: "failed" }))
              setDatasetErrors((prev) => ({ ...prev, [code]: message }))
              throw caught
            }
          })
        )

        for (const res of parseResults) {
          finalRunIds[res.code] = res.runId
        }
      }

      // Verify all dataset codes have resolved run IDs
      const missingCode = codes.find((code) => !finalRunIds[code])
      if (missingCode) {
        throw new Error(`Data for dataset ${missingCode} is missing.`)
      }

      // Step 2: Start Reconciliation
      setExecutionPhase("reconciling")
      const result = await api<{ id: string }>("/api/app/run", {
        method: "POST",
        body: JSON.stringify({
          profileId,
          parserRunIds: Object.fromEntries(
            codes.map((code) => [code, finalRunIds[code]])
          ),
        }),
        signal,
      })

      void queryClient.invalidateQueries({ queryKey: ["runs"] })
      navigate(`/reconciliation/runs/${result.id}/grid`)
    } catch (caught) {
      if (!signal.aborted) {
        setExecutionPhase("idle")
        setGlobalError(
          caught instanceof Error ? caught.message : "Reconciliation failed to start."
        )
      }
    }
  }

  // Readiness validation
  const isProfileApproved = profile.data?.status === "Approved"
  const hasEnoughDatasets = codes.length >= 2
  const readyCount = codes.filter((code) => {
    const hasData = Boolean(stagedFiles[code] || selectedRunIds[code])
    const hasParser = Boolean(selectedParserIds[code])
    return hasData && hasParser
  }).length
  const allDatasetsSatisfied = codes.length > 0 && readyCount === codes.length

  const canRun =
    Boolean(profileId) &&
    isProfileApproved &&
    hasEnoughDatasets &&
    allDatasetsSatisfied &&
    executionPhase === "idle"

  return (
    <section className="mx-auto flex w-full max-w-5xl flex-col gap-6 pb-20">
      {/* Top Navigation & Header */}
      <div>
        <Link
          to="/reconciliation/runs"
          className="mb-2 inline-flex items-center gap-1.5 text-xs text-muted-foreground transition-colors hover:text-foreground"
        >
          <ArrowLeft className="size-3.5" />
          <span>Back to runs</span>
        </Link>
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold tracking-tight">Create Reconciliation Run</h1>
          <p className="text-sm text-muted-foreground">
            Select an approved profile and provide the required dataset files. File parsing and reconciliation will execute seamlessly.
          </p>
        </div>
      </div>

      {/* Profile Selector Card */}
      <div className="rounded-xl border bg-card p-5 shadow-xs">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2">
              <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                Reconciliation Profile
              </span>
            </div>
            {selectedPackage ? (
              <div className="mt-1 flex flex-wrap items-center gap-2.5">
                <h2 className="text-lg font-semibold text-foreground">
                  {selectedPackage.profileName}
                </h2>
                <span className="inline-flex items-center rounded-full border border-emerald-500/20 bg-emerald-500/10 px-2.5 py-0.5 text-xs font-medium text-emerald-600 dark:text-emerald-400">
                  Approved
                </span>
                <span className="inline-flex items-center rounded-md bg-muted px-2 py-0.5 text-xs text-muted-foreground">
                  v{selectedPackage.profileVersion}
                </span>
              </div>
            ) : (
              <p className="mt-1 text-sm text-muted-foreground">
                Select an approved profile to run.
              </p>
            )}
          </div>

          <div className="w-full sm:w-80">
            <select
              aria-label="Select reconciliation profile"
              className={cn(inputClass, "h-9 rounded-lg text-sm")}
              value={profileId}
              disabled={executionPhase !== "idle"}
              onChange={(e) => handleProfileChange(e.target.value)}
            >
              <option value="">Select an approved profile...</option>
              {approvedPackages.map((item) => (
                <option key={item.profileId} value={item.profileId}>
                  {item.profileName} · v{item.profileVersion}
                </option>
              ))}
            </select>
          </div>
        </div>

        {/* Profile summary details when selected */}
        {definition && (
          <div className="mt-4 flex flex-wrap items-center gap-4 border-t border-border/50 pt-3 text-xs text-muted-foreground">
            <div className="flex items-center gap-1.5">
              <Layers className="size-3.5 text-muted-foreground" />
              <span>Datasets ({codes.length}):</span>
              <div className="flex items-center gap-1">
                {codes.map((c) => (
                  <span
                    key={c}
                    className="rounded bg-muted px-1.5 py-0.5 font-mono text-[11px] text-foreground"
                  >
                    {c}
                  </span>
                ))}
              </div>
            </div>
            <div className="flex items-center gap-1.5">
              <span>Matching rules:</span>
              <span className="font-semibold text-foreground">
                {definition.rules.length}
              </span>
            </div>
          </div>
        )}

        {approvedPackages.length === 0 && !packages.isLoading && (
          <div className="mt-4 rounded-lg border border-amber-500/20 bg-amber-500/10 p-3 text-xs text-amber-700 dark:text-amber-400">
            No approved profiles available.{" "}
            <Link className="font-medium underline" to="/configuration/manual/profiles">
              Create and approve a profile
            </Link>{" "}
            before creating a reconciliation run.
          </div>
        )}
      </div>

      {/* Dataset Inputs Section */}
      {profileId && codes.length > 0 && (
        <div className="flex flex-col gap-4">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-base font-semibold">Dataset Sources</h2>
              <p className="text-xs text-muted-foreground">
                Drop or select a file for each dataset. Files are parsed automatically when reconciliation starts.
              </p>
            </div>
            <div className="flex items-center gap-1.5 text-xs font-medium">
              <span
                className={cn(
                  "inline-flex items-center gap-1 rounded-full border px-2.5 py-0.5 transition-colors",
                  allDatasetsSatisfied
                    ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400"
                    : "border-border bg-muted text-muted-foreground"
                )}
              >
                {allDatasetsSatisfied ? (
                  <Check className="size-3" />
                ) : (
                  <span className="size-1.5 rounded-full bg-amber-500" />
                )}
                {readyCount} of {codes.length} ready
              </span>
            </div>
          </div>

          <div className="grid grid-cols-1 gap-5 lg:grid-cols-2">
            {codes.map((code) => {
              const dataset = datasets.data?.find(
                (item) => item.code.trim().toUpperCase() === code.trim().toUpperCase()
              )

              if (!dataset) {
                return (
                  <div
                    key={code}
                    className="flex flex-col justify-between rounded-xl border border-dashed border-border bg-card/50 p-5 shadow-xs"
                  >
                    <div>
                      <div className="flex items-center gap-2 border-b border-border/50 pb-3">
                        <span className="rounded bg-muted px-2 py-0.5 font-mono text-xs font-semibold text-foreground">
                          {code}
                        </span>
                      </div>
                      <div className="py-6 text-center">
                        <p className="text-xs text-muted-foreground">
                          Dataset <span className="font-semibold text-foreground">{code}</span> is not found in the canonical catalog.
                        </p>
                      </div>
                    </div>
                    <div className="pt-2 text-center">
                      <Link
                        to="/configuration/manual/datasets"
                        className="inline-flex items-center text-xs font-medium text-primary hover:underline"
                      >
                        Configure dataset schema &rarr;
                      </Link>
                    </div>
                  </div>
                )
              }

              return (
                <ParserRunInput
                  key={`${profileId}-${code}`}
                  datasetId={dataset.id}
                  dataset={dataset}
                  code={code}
                  parsers={parsers.data ?? []}
                  selectedParserId={selectedParserIds[code] ?? ""}
                  onSelectParserId={(id) =>
                    setSelectedParserIds((current) => ({ ...current, [code]: id }))
                  }
                  stagedFile={stagedFiles[code] ?? null}
                  onStageFile={(file) =>
                    setStagedFiles((current) => ({ ...current, [code]: file }))
                  }
                  selectedRunId={selectedRunIds[code] ?? ""}
                  onSelectRunId={(runId) =>
                    setSelectedRunIds((current) => ({ ...current, [code]: runId }))
                  }
                  status={datasetStatuses[code] ?? "idle"}
                  error={datasetErrors[code] ?? null}
                  disabled={executionPhase !== "idle"}
                />
              )
            })}
          </div>
        </div>
      )}

      {/* Error Feedback */}
      {[packages.error, profile.error, datasets.error, parsers.error]
        .filter(Boolean)
        .map((error, index) => (
          <ErrorNote key={index} message={(error as Error).message} />
        ))}

      {globalError && <ErrorNote message={globalError} />}

      {/* Sticky Bottom Action Bar */}
      <div className="sticky bottom-4 z-10 rounded-xl border bg-card/95 p-4 shadow-lg backdrop-blur-md">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div className="text-xs">
            {executionPhase === "parsing" ? (
              <div className="flex items-center gap-2 font-medium text-amber-600 dark:text-amber-400">
                <Loader2 className="size-4 animate-spin" />
                <span>Step 1 of 2: Uploading and parsing dataset files...</span>
              </div>
            ) : executionPhase === "reconciling" ? (
              <div className="flex items-center gap-2 font-medium text-primary">
                <Loader2 className="size-4 animate-spin" />
                <span>Step 2 of 2: Starting reconciliation...</span>
              </div>
            ) : !profileId ? (
              <span className="text-muted-foreground">
                Select an approved reconciliation profile above to get started.
              </span>
            ) : !allDatasetsSatisfied ? (
              <span className="text-muted-foreground">
                Provide data for all {codes.length} datasets to enable reconciliation.
              </span>
            ) : (
              <div className="flex items-center gap-2 font-medium text-emerald-600 dark:text-emerald-400">
                <Check className="size-4" />
                <span>All {codes.length} datasets ready. Click below to start reconciliation.</span>
              </div>
            )}
          </div>

          <div className="flex items-center gap-2.5 shrink-0">
            <Button
              type="button"
              variant="outline"
              disabled={executionPhase !== "idle"}
              render={<Link to="/reconciliation/runs" />}
            >
              Cancel
            </Button>
            <Button
              type="button"
              disabled={!canRun}
              onClick={() => void handleStartReconciliation()}
              className="gap-2 px-5 font-semibold"
            >
              {executionPhase === "parsing" ? (
                <>
                  <Loader2 className="size-4 animate-spin" />
                  <span>Parsing files...</span>
                </>
              ) : executionPhase === "reconciling" ? (
                <>
                  <Loader2 className="size-4 animate-spin" />
                  <span>Reconciling...</span>
                </>
              ) : (
                <>
                  <Play className="size-4 fill-current" />
                  <span>Run reconciliation</span>
                </>
              )}
            </Button>
          </div>
        </div>
      </div>
    </section>
  )
}
