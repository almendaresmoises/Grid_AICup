import { Button } from "@workspace/ui/components/button"
import { useMutation, useQuery } from "@tanstack/react-query"
import { useState } from "react"
import { Link, useNavigate, useSearchParams } from "react-router"
import { ErrorNote } from "@/components/error-note"
import { ParserRunInput } from "@/components/parser-run-input"
import { api } from "@/lib/api"
import type { CanonicalDataset, ParserConfiguration } from "@/lib/canonical"
import { parseProfile, type Package, type Profile } from "@/lib/types"
import { inputClass, labelClass } from "@/lib/ui"

export function CreateRunPage() {
  const [search] = useSearchParams()
  const [profileId, setProfileId] = useState(search.get("profileId") ?? "")
  const [inputs, setInputs] = useState<Record<string, string>>({})
  const navigate = useNavigate()
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
  const definition = profile.data?.approvedDefinitionJson
    ? parseProfile(profile.data.approvedDefinitionJson)
    : null
  const codes = definition?.datasets ?? []
  const run = useMutation({
    mutationFn: () =>
      api<{ id: string }>("/api/app/run", {
        method: "POST",
        body: JSON.stringify({
          profileId,
          parserRunIds: Object.fromEntries(
            codes.map((code) => [code, inputs[code]])
          ),
        }),
      }),
    onSuccess: (result) => navigate(`/reconciliation/runs/${result.id}/grid`),
  })
  const ready =
    codes.length >= 2 &&
    profile.data?.status === "Approved" &&
    codes.every((code) => inputs[code])
  return (
    <section className="flex flex-col gap-5">
      <div>
        <h1 className="text-2xl font-medium">Create reconciliation run</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Choose an approved profile and a processed file for each dataset, or
          upload files to parse now.
        </p>
      </div>
      <label className="flex flex-col gap-1.5">
        <span className={labelClass}>Profile</span>
        <select
          className={inputClass}
          value={profileId}
          disabled={run.isPending}
          onChange={(event) => {
            setProfileId(event.target.value)
            setInputs({})
          }}
        >
          <option value="">Select an approved profile</option>
          {(packages.data ?? [])
            .filter((item) => item.profileStatus === "Approved")
            .map((item) => (
              <option key={item.profileId} value={item.profileId}>
                {item.profileName} · version {item.profileVersion}
              </option>
            ))}
        </select>
      </label>
      {packages.data &&
      !packages.data.some((item) => item.profileStatus === "Approved") ? (
        <p className="text-sm">
          No approved profiles yet.{" "}
          <Link className="underline" to="/configuration/manual/profiles">
            Create and approve a profile
          </Link>{" "}
          first.
        </p>
      ) : null}
      <fieldset disabled={run.isPending} className="flex flex-col gap-4">
        {codes.map((code) => {
          const dataset = datasets.data?.find((item) => item.code === code)
          return dataset ? (
            <ParserRunInput
              key={`${profileId}-${code}`}
              datasetId={dataset.id}
              code={code}
              parsers={parsers.data ?? []}
              value={inputs[code]}
              onChange={(runId) =>
                setInputs((current) => ({ ...current, [code]: runId }))
              }
            />
          ) : (
            <p key={code} className="text-sm">
              Dataset {code} is not in the canonical catalog.{" "}
              <Link className="underline" to="/configuration/manual/datasets">
                Configure its schema and parser
              </Link>{" "}
              to select files.
            </p>
          )
        })}
      </fieldset>
      {[packages.error, profile.error, datasets.error, parsers.error, run.error]
        .filter(Boolean)
        .map((error, index) => (
          <ErrorNote key={index} message={(error as Error).message} />
        ))}
      <div className="flex gap-2">
        <Button disabled={!ready || run.isPending} onClick={() => run.mutate()}>
          {run.isPending ? "Reconciling…" : "Run profile"}
        </Button>
        <Button variant="outline" render={<Link to="/reconciliation/runs" />}>
          Back to runs
        </Button>
      </div>
    </section>
  )
}
