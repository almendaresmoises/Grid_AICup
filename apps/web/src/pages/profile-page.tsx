import { Button } from "@workspace/ui/components/button"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  ArrowDown,
  ArrowLeft,
  ArrowUp,
  Database,
  Pencil,
  Plus,
  Trash2,
} from "lucide-react"
import { useState } from "react"
import { Link, useNavigate, useParams } from "react-router"
import { ErrorNote } from "@/components/error-note"
import { Modal } from "@/components/modal"
import { api } from "@/lib/api"
import {
  emptyProfile,
  parseProfile,
  type Dataset,
  type KeySpec,
  type Profile,
  type ProfileSpec,
  type RuleSpec,
  type RunSummary,
} from "@/lib/types"
import { inputClass, labelClass, textareaClass } from "@/lib/ui"

export function ProfilePage() {
  const navigate = useNavigate()
  const { profileId = "" } = useParams()
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
  const [mapping, setMapping] = useState(false)
  const [editing, setEditing] = useState<number | "new" | null>(null)
  const [context, setContext] = useState("")
  const [error, setError] = useState("")
  const [notice, setNotice] = useState("")
  const [draftedWithAi, setDraftedWithAi] = useState(false)

  const [loadedDefinition, setLoadedDefinition] = useState<string | null>(null)
  if (profile.data && profile.data.definitionJson !== loadedDefinition) {
    setLoadedDefinition(profile.data.definitionJson)
    setSpec(parseProfile(profile.data.definitionJson))
  }

  const save = useMutation({
    mutationFn: (approve: boolean) =>
      api<Profile>(
        approve
          ? `/api/app/profile/${profileId}/approve`
          : `/api/app/profile/${profileId}`,
        {
          method: approve ? "POST" : "PUT",
          body: JSON.stringify({
            definitionJson: JSON.stringify(spec),
            createdWithAi:
              draftedWithAi || Boolean(profile.data?.createdWithAi),
          }),
        }
      ),
    onSuccess: async (saved) => {
      setError("")
      setNotice(
        saved.status === "Approved"
          ? `Approved version ${saved.version}`
          : "Draft saved"
      )
      await queryClient.invalidateQueries({ queryKey: ["profile", profileId] })
      await queryClient.invalidateQueries({ queryKey: ["packages"] })
      await queryClient.invalidateQueries({ queryKey: ["profile-registry"] })
    },
    onError: (caught) =>
      setError(caught instanceof Error ? caught.message : "Save failed"),
  })
  const remove = useMutation({
    mutationFn: () =>
      api(`/api/app/profile/${profileId}`, {
        method: "DELETE",
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["packages"] })
      await queryClient.invalidateQueries({ queryKey: ["profile-registry"] })
      navigate("/configuration/manual/profiles")
    },
    onError: (caught) =>
      setError(caught instanceof Error ? caught.message : "Delete failed"),
  })
  const propose = useMutation({
    mutationFn: () =>
      api<{ definitionJson: string }>("/api/app/assistant/propose", {
        method: "POST",
        body: JSON.stringify({ datasetCodes: spec.datasets, context }),
      }),
    onSuccess: (result) => {
      setSpec(parseProfile(result.definitionJson))
      setDraftedWithAi(true)
      setNotice(
        "Draft received. Save it to keep the AI origin, then approve when you are ready."
      )
      setError("")
    },
    onError: (caught) =>
      setError(caught instanceof Error ? caught.message : "Assistant failed"),
  })

  const catalog = datasets.data ?? []
  const mapped = spec.datasets.map(
    (code) =>
      catalog.find((dataset) => dataset.code === code) ?? {
        id: code,
        code,
        name: code,
        rowCount: 0,
        columns: [],
      }
  )

  function commit(next: ProfileSpec) {
    setSpec(next)
  }

  function toggleDataset(code: string) {
    const nextCodes = spec.datasets.includes(code)
      ? spec.datasets.filter((item) => item !== code)
      : [...spec.datasets, code]
    commit({
      ...spec,
      datasets: nextCodes,
      rules: spec.rules.map((rule) => alignRule(rule, nextCodes, catalog)),
    })
  }

  return (
    <section className="flex flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex items-start gap-3">
          <Button
            variant="outline"
            size="icon-sm"
            aria-label="Back to profiles"
            render={<Link to="/configuration/manual/profiles" />}
          >
            <ArrowLeft />
          </Button>
          <div>
            <h1 className="text-2xl font-medium">
              {profile.data?.packageName ?? "Profile"}
            </h1>
            <p className="mt-1 text-sm text-muted-foreground">
              {profile.data?.status ?? "Draft"} · version{" "}
              {profile.data?.version ?? 0}
              {profile.data?.hasBaseline ? " · original batch stored" : ""}
              {profile.data?.createdWithAi || draftedWithAi
                ? " · drafted with AI"
                : ""}
            </p>
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button
            variant="outline"
            size="sm"
            disabled={remove.isPending || save.isPending}
            onClick={() => {
              const name =
                profile.data?.packageName || profile.data?.name || "this profile"
              if (
                window.confirm(
                  `Are you sure you want to delete profile "${name}"? All associated runs, baselines, and match groups will be removed.`
                )
              ) {
                remove.mutate()
              }
            }}
          >
            <Trash2 className="text-destructive" />
            Delete profile
          </Button>
          <Button
            variant="outline"
            size="sm"
            disabled={profile.data?.status !== "Approved"}
            render={
              <Link to={`/reconciliation/runs/new?profileId=${profileId}`} />
            }
          >
            Create run
          </Button>
          <Button
            variant="outline"
            size="sm"
            disabled={save.isPending || remove.isPending}
            onClick={() => save.mutate(false)}
          >
            Save draft
          </Button>
          <Button
            size="sm"
            disabled={save.isPending || remove.isPending}
            onClick={() => save.mutate(true)}
          >
            Approve
          </Button>
        </div>
      </div>
      {error || profile.error || datasets.error ? (
        <ErrorNote
          message={
            error || ((profile.error || datasets.error) as Error).message
          }
        />
      ) : null}
      {notice ? <p className="text-sm">{notice}</p> : null}

      <section className="overflow-hidden border bg-card">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b px-4 py-3">
          <div>
            <h2 className="flex items-center gap-2 text-sm font-medium">
              <Database className="size-4" /> Participating datasets
              <span className="bg-muted px-2 py-0.5 font-mono text-[10px]">
                {mapped.length} mapped
              </span>
            </h2>
            <p className="text-xs text-muted-foreground">
              These uploaded datasets are the sides this profile can match.
            </p>
          </div>
          <Button
            size="sm"
            type="button"
            onClick={() => setMapping((open) => !open)}
          >
            <Plus /> Map datasets
          </Button>
        </div>
        {mapping ? (
          <div className="flex flex-wrap gap-3 border-b px-4 py-3">
            {catalog.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                Create canonical datasets under Manual Configuration before
                mapping them here.
              </p>
            ) : null}
            {catalog.map((dataset) => (
              <label
                key={dataset.id}
                className="flex items-center gap-2 text-sm"
              >
                <input
                  type="checkbox"
                  checked={spec.datasets.includes(dataset.code)}
                  onChange={() => toggleDataset(dataset.code)}
                />
                <span className="font-mono text-xs">{dataset.code}</span>
                <span className="text-muted-foreground">{dataset.name}</span>
              </label>
            ))}
          </div>
        ) : null}
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b bg-muted/40 text-left text-xs text-muted-foreground">
                <th className="px-4 py-2.5 font-medium">Dataset</th>
                <th className="px-4 py-2.5 font-medium">Code</th>
                <th className="px-4 py-2.5 font-medium">Rows</th>
                <th className="px-4 py-2.5 font-medium">Columns</th>
                <th className="px-4 py-2.5 text-right font-medium">
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {mapped.length === 0 ? (
                <tr>
                  <td
                    colSpan={5}
                    className="px-4 py-8 text-center text-muted-foreground"
                  >
                    No datasets mapped.
                  </td>
                </tr>
              ) : null}
              {mapped.map((dataset) => (
                <tr key={dataset.code} className="border-b last:border-0">
                  <td className="px-4 py-3 font-medium">{dataset.name}</td>
                  <td className="px-4 py-3 font-mono text-xs">
                    {dataset.code}
                  </td>
                  <td className="px-4 py-3 tabular-nums">
                    {dataset.rowCount.toLocaleString()}
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex flex-wrap gap-1">
                      {dataset.columns.map((column) => (
                        <span
                          key={column.name}
                          className="border px-1.5 py-0.5 font-mono text-[10px]"
                        >
                          {column.name}{" "}
                          <span className="text-muted-foreground">
                            {column.type}
                          </span>
                        </span>
                      ))}
                    </div>
                  </td>
                  <td className="px-4 py-3 text-right">
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label={`Remove ${dataset.code}`}
                      onClick={() => toggleDataset(dataset.code)}
                    >
                      <Trash2 />
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="overflow-hidden border bg-card">
        <div className="flex items-center justify-between border-b px-4 py-3">
          <div>
            <h2 className="text-sm font-medium">Rule passes</h2>
            <p className="text-xs text-muted-foreground">
              Passes run from top to bottom. Exact passes match rows. Aggregate
              passes sum a source into a target.
            </p>
          </div>
          <Button
            size="sm"
            type="button"
            disabled={spec.datasets.length < 2}
            onClick={() => setEditing("new")}
          >
            <Plus /> Add rule pass
          </Button>
        </div>
        {editing !== null ? (
          <RuleEditor
            rule={
              editing === "new" ? newRule(spec, catalog) : spec.rules[editing]
            }
            catalog={catalog}
            profileDatasets={spec.datasets}
            onCancel={() => setEditing(null)}
            onSave={(rule) => {
              const rules =
                editing === "new"
                  ? [...spec.rules, rule]
                  : spec.rules.map((item, index) =>
                      index === editing ? rule : item
                    )
              commit({ ...spec, rules })
              setEditing(null)
            }}
          />
        ) : null}
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b bg-muted/40 text-left text-xs text-muted-foreground">
                <th className="px-4 py-2.5 font-medium">#</th>
                <th className="px-4 py-2.5 font-medium">Rule</th>
                <th className="px-4 py-2.5 font-medium">Type</th>
                <th className="px-4 py-2.5 font-medium">Datasets</th>
                <th className="px-4 py-2.5 font-medium">Keys</th>
                <th className="px-4 py-2.5 text-right font-medium">
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {spec.rules.length === 0 ? (
                <tr>
                  <td
                    colSpan={6}
                    className="px-4 py-8 text-center text-muted-foreground"
                  >
                    No rule passes yet.
                  </td>
                </tr>
              ) : null}
              {spec.rules.map((rule, index) => (
                <tr
                  key={`${rule.id}-${index}`}
                  className="border-b last:border-0"
                >
                  <td className="px-4 py-3 tabular-nums">{index + 1}</td>
                  <td className="px-4 py-3 font-medium">{rule.id}</td>
                  <td className="px-4 py-3">
                    <span
                      className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                        rule.type === "partial"
                          ? "bg-amber-500/10 text-amber-600 dark:text-amber-400"
                          : rule.type === "aggregate"
                            ? "bg-blue-500/10 text-blue-600 dark:text-blue-400"
                            : "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400"
                      }`}
                    >
                      {rule.type}
                    </span>
                  </td>
                  <td className="px-4 py-3">{rule.datasets.join(", ")}</td>
                  <td className="px-4 py-3 text-xs text-muted-foreground">
                    <div className="flex flex-col gap-0.5">
                      <span>{rule.keys.map((key) => key.type).join(", ") || "—"}</span>
                      {rule.type === "partial" ? (
                        <span className="text-[11px] text-amber-600 dark:text-amber-400">
                          {rule.min_score ? `min_score: ${rule.min_score}` : ""}
                          {rule.keys.some((k) => k.expected_mismatch) ? " · divergent key" : ""}
                          {rule.keys.some((k) => k.algorithm === "damerau_levenshtein") ? " · Damerau-Levenshtein" : ""}
                        </span>
                      ) : null}
                    </div>
                  </td>
                  <td className="px-4 py-3 text-right">
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label="Move up"
                      disabled={index === 0}
                      onClick={() => move(index, -1)}
                    >
                      <ArrowUp />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label="Move down"
                      disabled={index === spec.rules.length - 1}
                      onClick={() => move(index, 1)}
                    >
                      <ArrowDown />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label={`Edit ${rule.id}`}
                      onClick={() => setEditing(index)}
                    >
                      <Pencil />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label={`Remove ${rule.id}`}
                      onClick={() =>
                        commit({
                          ...spec,
                          rules: spec.rules.filter((_, item) => item !== index),
                        })
                      }
                    >
                      <Trash2 />
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="grid gap-4 border bg-card p-4 lg:grid-cols-[minmax(0,1fr)_auto]">
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Business context for the assistant</span>
          <textarea
            className={textareaClass}
            value={context}
            onChange={(event) => setContext(event.target.value)}
          />
        </label>
        <div className="flex items-end">
          <Button
            variant="outline"
            disabled={propose.isPending || spec.datasets.length < 2}
            onClick={() => propose.mutate()}
          >
            {propose.isPending ? "Drafting…" : "Draft with AI"}
          </Button>
        </div>
      </section>

      <section className="overflow-hidden border bg-card">
        <div className="border-b px-4 py-3">
          <h2 className="text-sm font-medium">Runs</h2>
        </div>
        {runs.data?.length === 0 ? (
          <p className="px-4 py-6 text-sm text-muted-foreground">
            No runs yet.
          </p>
        ) : null}
        {runs.data?.map((item) => (
          <Link
            key={item.id}
            to={`/reconciliation/runs/${item.id}`}
            className="flex items-center justify-between border-b px-4 py-3 text-sm last:border-0 hover:bg-muted"
          >
            <span>Version {item.profileVersion}</span>
            <span className="text-muted-foreground">
              {item.matchedRecords.toLocaleString()} matched ·{" "}
              {item.unresolvedRecords.toLocaleString()} unresolved
            </span>
          </Link>
        ))}
      </section>
    </section>
  )

  function move(index: number, offset: number) {
    const rules = [...spec.rules]
    const target = index + offset
    const current = rules[index]
    rules[index] = rules[target]
    rules[target] = current
    commit({ ...spec, rules })
  }
}

function RuleEditor({
  rule,
  catalog,
  profileDatasets,
  onCancel,
  onSave,
}: {
  rule: RuleSpec
  catalog: Dataset[]
  profileDatasets: string[]
  onCancel: () => void
  onSave: (rule: RuleSpec) => void
}) {
  const [draft, setDraft] = useState(rule)
  const columnsFor = (code: string) =>
    catalog.find((dataset) => dataset.code === code)?.columns ?? []

  return (
    <Modal
      isOpen={true}
      onClose={onCancel}
      title={rule.id ? `Edit Rule Pass: ${draft.id || rule.id}` : `New Rule Pass: ${draft.id}`}
      description="Configure matching type (exact, aggregate, partial), participating datasets, and key mappings."
      size="4xl"
      footer={
        <div className="flex items-center justify-between w-full">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() =>
              setDraft({
                ...draft,
                keys: [...draft.keys, blankKey(draft.datasets, catalog)],
              })
            }
          >
            <Plus /> Add key
          </Button>
          <div className="flex items-center gap-2">
            <Button type="button" variant="outline" size="sm" onClick={onCancel}>
              Cancel
            </Button>
            <Button type="submit" form="rule-editor-form" size="sm">
              Save rule pass
            </Button>
          </div>
        </div>
      }
    >
      <form
        id="rule-editor-form"
        className="flex flex-col gap-4"
        onSubmit={(event) => {
          event.preventDefault()
          onSave(draft)
        }}
      >
      <div className="grid gap-3 sm:grid-cols-3">
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Rule id</span>
          <input
            className={inputClass}
            value={draft.id}
            onChange={(event) => setDraft({ ...draft, id: event.target.value })}
            required
          />
        </label>
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Type</span>
          <select
            className={inputClass}
            value={draft.type}
            onChange={(event) =>
              setDraft({
                ...draft,
                type: event.target.value as RuleSpec["type"],
                target: draft.datasets[1] ?? "",
                source: draft.datasets[0] ?? "",
                tolerance: draft.tolerance ?? "0",
              })
            }
          >
            <option value="exact">exact</option>
            <option value="aggregate">aggregate</option>
            <option value="partial">partial (fuzzy / divergent pass)</option>
          </select>
        </label>
        <fieldset className="flex flex-col gap-1.5">
          <legend className={labelClass}>Datasets in this pass</legend>
          <div className="flex flex-wrap gap-2">
            {profileDatasets.map((code) => (
              <label key={code} className="flex items-center gap-1 text-xs">
                <input
                  type="checkbox"
                  checked={draft.datasets.includes(code)}
                  onChange={() => {
                    const datasets = draft.datasets.includes(code)
                      ? draft.datasets.filter((item) => item !== code)
                      : [...draft.datasets, code]
                    setDraft(
                      alignRule(
                        { ...draft, datasets },
                        profileDatasets,
                        catalog
                      )
                    )
                  }}
                />
                {code}
              </label>
            ))}
          </div>
        </fieldset>
      </div>

      {draft.type === "partial" ? (
        <div className="grid gap-3 sm:grid-cols-2 rounded border bg-amber-500/5 p-3 border-amber-500/20">
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Pass min score threshold (0.10 - 1.00)</span>
            <input
              className={inputClass}
              type="number"
              step="0.05"
              min="0.10"
              max="1.00"
              value={draft.min_score ?? 0.8}
              onChange={(event) =>
                setDraft({ ...draft, min_score: Number(event.target.value) })
              }
            />
            <span className="text-[11px] text-muted-foreground">
              Weights balance between false positives and non-matching due to strict scores.
            </span>
          </label>
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Recon remarks template (optional)</span>
            <input
              className={inputClass}
              placeholder="e.g. Possible match with divergent key or fuzzy reference"
              value={draft.remarks ?? ""}
              onChange={(event) =>
                setDraft({ ...draft, remarks: event.target.value })
              }
            />
            <span className="text-[11px] text-muted-foreground">
              Remarks attached to matched records from this pass.
            </span>
          </label>
        </div>
      ) : null}

      {draft.type === "aggregate" ? (
        <div className="grid gap-3 sm:grid-cols-3">
          <SelectField
            label="Target"
            value={draft.target ?? ""}
            options={draft.datasets}
            onChange={(target) => setDraft({ ...draft, target })}
          />
          <SelectField
            label="Source"
            value={draft.source ?? ""}
            options={draft.datasets}
            onChange={(source) => setDraft({ ...draft, source })}
          />
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Tolerance</span>
            <input
              className={inputClass}
              value={draft.tolerance ?? "0"}
              onChange={(event) =>
                setDraft({ ...draft, tolerance: event.target.value })
              }
            />
          </label>
          <SelectField
            label="Target amount"
            value={draft.target_amount ?? ""}
            options={columnsFor(draft.target ?? "").map(
              (column) => column.name
            )}
            onChange={(target_amount) => setDraft({ ...draft, target_amount })}
          />
          <SelectField
            label="Source amount"
            value={draft.source_amount ?? ""}
            options={columnsFor(draft.source ?? "").map(
              (column) => column.name
            )}
            onChange={(source_amount) => setDraft({ ...draft, source_amount })}
          />
        </div>
      ) : null}
      {draft.keys.map((key, keyIndex) => (
        <div key={keyIndex} className="grid gap-3 border p-3 sm:grid-cols-2">
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Key type</span>
            <select
              className={inputClass}
              value={key.type}
              onChange={(event) =>
                replaceKey(keyIndex, {
                  ...key,
                  type: event.target.value as KeySpec["type"],
                })
              }
            >
              <option value="text">text</option>
              <option value="amount">amount</option>
              <option value="date">date</option>
            </select>
          </label>
          {draft.datasets.map((code) => (
            <SelectField
              key={code}
              label={code}
              value={key.fields[code] ?? ""}
              options={columnsFor(code).map((column) => column.name)}
              onChange={(column) =>
                replaceKey(keyIndex, {
                  ...key,
                  fields: { ...key.fields, [code]: column },
                })
              }
            />
          ))}

          {draft.type === "partial" ? (
            <div className="grid gap-2 border-t pt-2 sm:col-span-2 sm:grid-cols-3">
              <label className="flex flex-col gap-1">
                <span className={labelClass}>Match mode</span>
                <select
                  className={inputClass}
                  value={
                    key.expected_mismatch
                      ? "divergent"
                      : key.match_mode ?? "exact"
                  }
                  onChange={(e) => {
                    const mode = e.target.value
                    if (mode === "divergent") {
                      replaceKey(keyIndex, {
                        ...key,
                        match_mode: "divergent",
                        expected_mismatch: true,
                      })
                    } else if (mode === "fuzzy") {
                      replaceKey(keyIndex, {
                        ...key,
                        match_mode: "fuzzy",
                        expected_mismatch: false,
                        algorithm:
                          key.type === "text"
                            ? "damerau_levenshtein"
                            : key.type === "amount"
                              ? "numeric_best_practice"
                              : "date_proximity",
                        min_score: key.min_score ?? 0.85,
                      })
                    } else {
                      replaceKey(keyIndex, {
                        ...key,
                        match_mode: "exact",
                        expected_mismatch: false,
                      })
                    }
                  }}
                >
                  <option value="exact">Exact key (anchor)</option>
                  <option value="fuzzy">Fuzzy similarity</option>
                  <option value="divergent">Expected mismatch</option>
                </select>
              </label>

              {key.match_mode === "fuzzy" ? (
                <>
                  <label className="flex flex-col gap-1">
                    <span className={labelClass}>Algorithm</span>
                    <select
                      className={inputClass}
                      value={
                        key.algorithm ??
                        (key.type === "text"
                          ? "damerau_levenshtein"
                          : "numeric_best_practice")
                      }
                      onChange={(e) =>
                        replaceKey(keyIndex, {
                          ...key,
                          algorithm: e.target.value as KeySpec["algorithm"],
                        })
                      }
                    >
                      {key.type === "text" ? (
                        <option value="damerau_levenshtein">
                          Damerau-Levenshtein (edit + transposition)
                        </option>
                      ) : key.type === "amount" ? (
                        <option value="numeric_best_practice">
                          Numeric best practice (relative / tolerance)
                        </option>
                      ) : (
                        <option value="date_proximity">
                          Date proximity (calendar day distance)
                        </option>
                      )}
                    </select>
                  </label>
                  <label className="flex flex-col gap-1">
                    <span className={labelClass}>Key min score</span>
                    <input
                      type="number"
                      step="0.05"
                      min="0.10"
                      max="1.00"
                      className={inputClass}
                      value={key.min_score ?? 0.85}
                      onChange={(e) =>
                        replaceKey(keyIndex, {
                          ...key,
                          min_score: Number(e.target.value),
                        })
                      }
                    />
                  </label>
                </>
              ) : null}

              {key.expected_mismatch ? (
                <div className="col-span-2 flex items-center text-xs text-amber-600 dark:text-amber-400">
                  <span>
                    Anchor keys must match; this key is expected to differ and generates recon remarks ("Possible match but key is not matched").
                  </span>
                </div>
              ) : null}
            </div>
          ) : null}

          {key.type === "text" ? (
            <label className="flex flex-col gap-1.5 sm:col-span-2">
              <span className={labelClass}>Strip prefixes</span>
              <input
                className={inputClass}
                placeholder="CARD/, BANK/"
                value={formatPrefixes(key)}
                onChange={(event) =>
                  replaceKey(
                    keyIndex,
                    applyPrefixes(key, draft.datasets, event.target.value)
                  )
                }
              />
            </label>
          ) : null}
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() =>
              setDraft({
                ...draft,
                keys: draft.keys.filter((_, index) => index !== keyIndex),
              })
            }
          >
            Remove key
          </Button>
        </div>
      ))}
      </form>
    </Modal>
  )

  function replaceKey(index: number, key: KeySpec) {
    setDraft({
      ...draft,
      keys: draft.keys.map((item, itemIndex) =>
        itemIndex === index ? key : item
      ),
    })
  }
}

function newRule(spec: ProfileSpec, catalog: Dataset[]): RuleSpec {
  return {
    id: `RULE_${spec.rules.length + 1}`,
    type: "exact",
    datasets: spec.datasets,
    keys: [blankKey(spec.datasets, catalog)],
  }
}

function blankKey(codes: string[], datasets: Dataset[]): KeySpec {
  const fields: Record<string, string> = {}
  for (const code of codes)
    fields[code] =
      datasets.find((dataset) => dataset.code === code)?.columns[0]?.name ?? ""
  return { type: "text", fields }
}

function alignRule(
  rule: RuleSpec,
  codes: string[],
  datasets: Dataset[]
): RuleSpec {
  const kept = rule.datasets.filter((code) => codes.includes(code))
  const nextDatasets = kept.length >= 2 ? kept : codes
  return {
    ...rule,
    datasets: nextDatasets,
    target: nextDatasets.includes(rule.target ?? "")
      ? rule.target
      : nextDatasets[1],
    source: nextDatasets.includes(rule.source ?? "")
      ? rule.source
      : nextDatasets[0],
    keys: rule.keys.map((key) => ({
      ...key,
      fields: Object.fromEntries(
        nextDatasets.map((code) => [
          code,
          key.fields[code] ??
            datasets.find((dataset) => dataset.code === code)?.columns[0]
              ?.name ??
            "",
        ])
      ),
    })),
  }
}

function formatPrefixes(key: KeySpec) {
  return Object.values(key.strip_prefixes ?? {})
    .flat()
    .join(", ")
}

function applyPrefixes(
  key: KeySpec,
  datasets: string[],
  text: string
): KeySpec {
  const values = text
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean)
  if (values.length === 0) {
    const rest = { ...key }
    delete rest.strip_prefixes
    return rest
  }
  return {
    ...key,
    strip_prefixes: Object.fromEntries(datasets.map((code) => [code, values])),
  }
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
      <select
        className={inputClass}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      >
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
