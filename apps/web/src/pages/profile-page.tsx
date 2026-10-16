import { Button } from "@workspace/ui/components/button"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { useEffect, useState } from "react"
import { Link, useNavigate, useParams } from "react-router"
import { api } from "@/lib/api"
import type { Dataset, KeySpec, Profile, ProfileSpec, RuleSpec, RunSummary } from "@/lib/types"
import { emptyProfile, parseProfile } from "@/lib/types"
import { ErrorNote } from "@/components/error-note"
import { inputClass, labelClass, textareaClass } from "@/lib/ui"

export function ProfilePage() {
  const { profileId = "" } = useParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const profile = useQuery({
    queryKey: ["profile", profileId],
    queryFn: () => api<Profile>(`/api/app/profile/${profileId}`),
  })
  const datasets = useQuery({
    queryKey: ["datasets"],
    queryFn: () => api<Dataset[]>("/api/app/dataset"),
  })
  const runs = useQuery({
    queryKey: ["runs", profileId],
    queryFn: () => api<RunSummary[]>(`/api/app/run?profileId=${profileId}`),
  })
  const [spec, setSpec] = useState<ProfileSpec>(emptyProfile())
  const [jsonText, setJsonText] = useState("")
  const [context, setContext] = useState("")
  const [error, setError] = useState("")
  const [notice, setNotice] = useState("")

  useEffect(() => {
    if (!profile.data) {
      return
    }
    const parsed = parseProfile(profile.data.definitionJson)
    setSpec(parsed)
    setJsonText(JSON.stringify(parsed, null, 2))
  }, [profile.data])

  function commit(next: ProfileSpec) {
    setSpec(next)
    setJsonText(JSON.stringify(next, null, 2))
  }

  const save = useMutation({
    mutationFn: (approve: boolean) =>
      api<Profile>(approve ? `/api/app/profile/${profileId}/approve` : `/api/app/profile/${profileId}`, {
        method: approve ? "POST" : "PUT",
        body: JSON.stringify({ definitionJson: JSON.stringify(spec) }),
      }),
    onSuccess: async (saved) => {
      setError("")
      setNotice(saved.status === "Approved" ? `Approved version ${saved.version}` : "Draft saved")
      await queryClient.invalidateQueries({ queryKey: ["profile", profileId] })
      await queryClient.invalidateQueries({ queryKey: ["packages"] })
    },
    onError: (caught) => setError(caught instanceof Error ? caught.message : "Save failed"),
  })

  const propose = useMutation({
    mutationFn: () =>
      api<{ definitionJson: string }>("/api/app/assistant/propose", {
        method: "POST",
        body: JSON.stringify({ datasetCodes: spec.datasets, context }),
      }),
    onSuccess: (result) => {
      const parsed = parseProfile(result.definitionJson)
      commit(parsed)
      setNotice("Draft received. It is not active until you approve it.")
      setError("")
    },
    onError: (caught) => setError(caught instanceof Error ? caught.message : "Assistant failed"),
  })

  const run = useMutation({
    mutationFn: () =>
      api<{ id: string }>("/api/app/run", {
        method: "POST",
        body: JSON.stringify({ profileId }),
      }),
    onSuccess: (created) => navigate(`/reconciliation/runs/${created.id}`),
    onError: (caught) => setError(caught instanceof Error ? caught.message : "Run failed"),
  })

  const columnsFor = (code: string) => datasets.data?.find((item) => item.code === code)?.columns ?? []

  return (
    <section className="flex flex-col gap-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-medium">{profile.data?.packageName ?? "Profile"}</h1>
          <p className="text-muted-foreground mt-1 text-sm">
            {profile.data?.status ?? "Draft"} · approved version {profile.data?.version ?? 0}
            {profile.data?.hasBaseline ? " · original batch stored" : ""}
          </p>
        </div>
        <Button
          variant="outline"
          disabled={profile.data?.status !== "Approved" || run.isPending}
          onClick={() => run.mutate()}
        >
          Run approved profile
        </Button>
      </div>
      {error ? <ErrorNote message={error} /> : null}
      {notice ? <p className="text-sm">{notice}</p> : null}

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="flex flex-col gap-4 border bg-card p-5">
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Profile name</span>
            <input className={inputClass} value={spec.name} onChange={(event) => commit({ ...spec, name: event.target.value })} />
          </label>
          <fieldset className="flex flex-col gap-2">
            <legend className={labelClass}>Datasets</legend>
            <div className="flex flex-wrap gap-3">
              {datasets.data?.map((dataset) => {
                const checked = spec.datasets.includes(dataset.code)
                return (
                  <label key={dataset.id} className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={checked}
                      onChange={() => {
                        const nextCodes = checked
                          ? spec.datasets.filter((code) => code !== dataset.code)
                          : [...spec.datasets, dataset.code]
                        commit({
                          ...spec,
                          datasets: nextCodes,
                          rules: spec.rules.map((rule) => alignRule(rule, nextCodes, datasets.data ?? [])),
                        })
                      }}
                    />
                    {dataset.code}
                  </label>
                )
              })}
            </div>
          </fieldset>

          {spec.rules.map((rule, ruleIndex) => (
            <article key={`${rule.id}-${ruleIndex}`} className="flex flex-col gap-3 border p-3">
              <div className="grid gap-3 md:grid-cols-3">
                <label className="flex flex-col gap-1.5">
                  <span className={labelClass}>Rule id</span>
                  <input
                    className={inputClass}
                    value={rule.id}
                    onChange={(event) => updateRule(ruleIndex, { ...rule, id: event.target.value })}
                  />
                </label>
                <label className="flex flex-col gap-1.5">
                  <span className={labelClass}>Type</span>
                  <select
                    className={inputClass}
                    value={rule.type}
                    onChange={(event) =>
                      updateRule(ruleIndex, {
                        ...rule,
                        type: event.target.value as RuleSpec["type"],
                        target: rule.datasets[1] ?? "",
                        source: rule.datasets[0] ?? "",
                        tolerance: rule.tolerance ?? "0",
                      })
                    }
                  >
                    <option value="exact">exact</option>
                    <option value="aggregate">aggregate</option>
                  </select>
                </label>
                <div className="flex items-end">
                  <Button variant="ghost" size="sm" onClick={() => commit({ ...spec, rules: spec.rules.filter((_, index) => index !== ruleIndex) })}>
                    Remove rule
                  </Button>
                </div>
              </div>
              {rule.type === "aggregate" ? (
                <div className="grid gap-3 md:grid-cols-3">
                  <SelectField label="Target" value={rule.target ?? ""} options={rule.datasets} onChange={(target) => updateRule(ruleIndex, { ...rule, target })} />
                  <SelectField label="Source" value={rule.source ?? ""} options={rule.datasets} onChange={(source) => updateRule(ruleIndex, { ...rule, source })} />
                  <label className="flex flex-col gap-1.5">
                    <span className={labelClass}>Tolerance</span>
                    <input className={inputClass} value={rule.tolerance ?? "0"} onChange={(event) => updateRule(ruleIndex, { ...rule, tolerance: event.target.value })} />
                  </label>
                  <SelectField
                    label="Target amount"
                    value={rule.target_amount ?? ""}
                    options={columnsFor(rule.target ?? "").map((column) => column.name)}
                    onChange={(target_amount) => updateRule(ruleIndex, { ...rule, target_amount })}
                  />
                  <SelectField
                    label="Source amount"
                    value={rule.source_amount ?? ""}
                    options={columnsFor(rule.source ?? "").map((column) => column.name)}
                    onChange={(source_amount) => updateRule(ruleIndex, { ...rule, source_amount })}
                  />
                </div>
              ) : null}
              {rule.keys.map((key, keyIndex) => (
                <div key={keyIndex} className="grid gap-3 border p-3 md:grid-cols-2">
                  <label className="flex flex-col gap-1.5">
                    <span className={labelClass}>Key type</span>
                    <select
                      className={inputClass}
                      value={key.type}
                      onChange={(event) => replaceKey(ruleIndex, keyIndex, { ...key, type: event.target.value as KeySpec["type"] })}
                    >
                      <option value="text">text</option>
                      <option value="amount">amount</option>
                      <option value="date">date</option>
                    </select>
                  </label>
                  {rule.datasets.map((code) => (
                    <SelectField
                      key={code}
                      label={code}
                      value={key.fields[code] ?? ""}
                      options={columnsFor(code).map((column) => column.name)}
                      onChange={(column) =>
                        replaceKey(ruleIndex, keyIndex, { ...key, fields: { ...key.fields, [code]: column } })
                      }
                    />
                  ))}
                  {key.type === "text" ? (
                    <label className="flex flex-col gap-1.5 md:col-span-2">
                      <span className={labelClass}>Strip prefixes</span>
                      <input
                        className={inputClass}
                        placeholder="CARD/, BANK/"
                        value={formatPrefixes(key)}
                        onChange={(event) => replaceKey(ruleIndex, keyIndex, applyPrefixes(key, rule.datasets, event.target.value))}
                      />
                    </label>
                  ) : null}
                  <Button variant="ghost" size="sm" onClick={() => updateRule(ruleIndex, { ...rule, keys: rule.keys.filter((_, index) => index !== keyIndex) })}>
                    Remove key
                  </Button>
                </div>
              ))}
              <Button variant="outline" size="sm" onClick={() => updateRule(ruleIndex, { ...rule, keys: [...rule.keys, blankKey(rule.datasets, datasets.data ?? [])] })}>
                Add key
              </Button>
            </article>
          ))}
          <Button
            variant="outline"
            onClick={() =>
              commit({
                ...spec,
                rules: [
                  ...spec.rules,
                  {
                    id: `RULE_${spec.rules.length + 1}`,
                    type: "exact",
                    datasets: spec.datasets,
                    keys: [blankKey(spec.datasets, datasets.data ?? [])],
                  },
                ],
              })
            }
          >
            Add rule
          </Button>
        </div>

        <div className="flex flex-col gap-3">
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Business context for the assistant</span>
            <textarea className={textareaClass} value={context} onChange={(event) => setContext(event.target.value)} />
          </label>
          <Button variant="secondary" disabled={propose.isPending || spec.datasets.length < 2} onClick={() => propose.mutate()}>
            Draft with AI
          </Button>
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Profile JSON</span>
            <textarea
              className={textareaClass}
              value={jsonText}
              onChange={(event) => setJsonText(event.target.value)}
              onBlur={() => {
                try {
                  commit(parseProfile(jsonText))
                  setError("")
                } catch {
                  setError("Profile JSON could not be read. The form still has the last valid draft.")
                }
              }}
            />
          </label>
          <div className="flex gap-2">
            <Button variant="outline" disabled={save.isPending} onClick={() => save.mutate(false)}>
              Save draft
            </Button>
            <Button disabled={save.isPending} onClick={() => save.mutate(true)}>
              Approve
            </Button>
          </div>
          <p className="text-muted-foreground text-xs">
            Approval checks the rules against the uploaded columns. After the first approval, a later version must still match every group from that original batch.
          </p>
        </div>
      </div>

      <div>
        <h2 className="text-lg font-medium">Runs</h2>
        <div className="mt-3 flex flex-col gap-2">
          {runs.data?.map((item) => (
            <Link key={item.id} to={`/reconciliation/runs/${item.id}`} className="border px-3 py-2 text-sm hover:bg-muted">
              Version {item.profileVersion} · {item.matchedRecords} matched · {item.unresolvedRecords} unresolved
            </Link>
          ))}
          {runs.data?.length === 0 ? <p className="text-muted-foreground text-sm">No runs yet.</p> : null}
        </div>
      </div>
    </section>
  )

  function updateRule(index: number, rule: RuleSpec) {
    commit({ ...spec, rules: spec.rules.map((item, itemIndex) => (itemIndex === index ? rule : item)) })
  }

  function replaceKey(ruleIndex: number, keyIndex: number, key: KeySpec) {
    const rule = spec.rules[ruleIndex]
    updateRule(ruleIndex, { ...rule, keys: rule.keys.map((item, index) => (index === keyIndex ? key : item)) })
  }
}

function blankKey(codes: string[], datasets: Dataset[]): KeySpec {
  const fields: Record<string, string> = {}
  for (const code of codes) {
    fields[code] = datasets.find((dataset) => dataset.code === code)?.columns[0]?.name ?? ""
  }
  return { type: "text", fields }
}

function alignRule(rule: RuleSpec, codes: string[], datasets: Dataset[]): RuleSpec {
  const datasetsForRule = rule.datasets.filter((code) => codes.includes(code))
  const nextDatasets = datasetsForRule.length >= 2 ? datasetsForRule : codes
  return {
    ...rule,
    datasets: nextDatasets,
    target: nextDatasets.includes(rule.target ?? "") ? rule.target : nextDatasets[1],
    source: nextDatasets.includes(rule.source ?? "") ? rule.source : nextDatasets[0],
    keys: rule.keys.map((key) => ({
      ...key,
      fields: Object.fromEntries(nextDatasets.map((code) => [code, key.fields[code] ?? datasets.find((dataset) => dataset.code === code)?.columns[0]?.name ?? ""])),
    })),
  }
}

function formatPrefixes(key: KeySpec) {
  const prefixes = key.strip_prefixes ?? {}
  return Object.entries(prefixes)
    .flatMap(([, values]) => values)
    .join(", ")
}

function applyPrefixes(key: KeySpec, datasets: string[], text: string): KeySpec {
  const values = text
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean)
  if (values.length === 0) {
    const rest = { ...key }
    delete rest.strip_prefixes
    return rest
  }
  const strip_prefixes: Record<string, string[]> = {}
  for (const code of datasets) {
    strip_prefixes[code] = values
  }
  return { ...key, strip_prefixes }
}

function SelectField({
  label,
  value,
  options,
  onChange,
}: {
  label: string
  value: string
  options: string[]
  onChange: (value: string) => void
}) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className={labelClass}>{label}</span>
      <select className={inputClass} value={value} onChange={(event) => onChange(event.target.value)}>
        <option value="">Select</option>
        {options.map((option) => (
          <option key={option} value={option}>
            {option}
          </option>
        ))}
      </select>
    </label>
  )
}
