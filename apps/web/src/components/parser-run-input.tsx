import { Button } from "@workspace/ui/components/button"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useEffect, useRef, useState } from "react"
import { Link } from "react-router"
import { ErrorNote } from "@/components/error-note"
import { api } from "@/lib/api"
import type { ParserConfiguration, ParserRun } from "@/lib/canonical"
import { uploadAndParse } from "@/lib/parser-runs"
import { readStudio } from "@/lib/parser-studio"
import { inputClass, labelClass } from "@/lib/ui"

export function ParserRunInput({
  datasetId,
  code,
  parsers,
  value,
  onChange,
}: {
  datasetId: string
  code: string
  parsers: ParserConfiguration[]
  value?: string
  onChange: (runId: string) => void
}) {
  const queryClient = useQueryClient()
  const available = parsers.filter(
    (parser) => readStudio(parser.pipelineJson)?.studio.datasetId === datasetId
  )
  const [choice, setChoice] = useState("")
  const parserId =
    choice ||
    available.find((parser) => parser.isActive)?.id ||
    available[0]?.id ||
    ""
  const parser = available.find((item) => item.id === parserId)
  const [file, setFile] = useState<File | null>(null)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState("")
  const controller = useRef<AbortController | null>(null)
  useEffect(() => () => controller.current?.abort(), [])
  const runs = useQuery({
    queryKey: ["parser-runs", parserId],
    queryFn: () => api<ParserRun[]>(`/api/parsers/${parserId}/runs`),
    enabled: Boolean(parserId),
    refetchInterval: (query) =>
      query.state.data?.some((run) => run.status === "Queued") ? 2000 : false,
  })

  async function upload() {
    if (!file || !parser) return
    setPending(true)
    setError("")
    onChange("")
    const active = new AbortController()
    controller.current = active
    try {
      const run = await uploadAndParse(parser.id, file, active.signal)
      await queryClient.invalidateQueries({
        queryKey: ["parser-runs", parser.id],
      })
      await queryClient.invalidateQueries({ queryKey: ["datasets"] })
      onChange(run.id)
    } catch (caught) {
      if (!active.signal.aborted)
        setError(
          caught instanceof Error
            ? caught.message
            : "Could not parse this file."
        )
    } finally {
      if (!active.signal.aborted) setPending(false)
      void queryClient.invalidateQueries({
        queryKey: ["parser-runs", parser.id],
      })
    }
  }

  return (
    <fieldset className="flex flex-col gap-3 border p-4" disabled={pending}>
      <legend className="px-1 font-medium">{code}</legend>
      {available.length === 0 ? (
        <p className="text-sm">
          Create and publish a{" "}
          <Link className="underline" to="/configuration/manual/parsers/new">
            parser for this dataset
          </Link>{" "}
          to upload files.
        </p>
      ) : (
        <>
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Parser</span>
            <select
              className={inputClass}
              value={parserId}
              onChange={(event) => {
                setChoice(event.target.value)
                onChange("")
              }}
            >
              {available.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name}
                  {item.isActive ? "" : " (draft)"}
                </option>
              ))}
            </select>
          </label>
          <label className="flex flex-col gap-1.5">
            <span className={labelClass}>Processed file</span>
            <select
              className={inputClass}
              value={value ?? ""}
              onChange={(event) => onChange(event.target.value)}
            >
              <option value="">Select a completed file</option>
              {(runs.data ?? []).map((run) => (
                <option
                  key={run.id}
                  value={run.id}
                  disabled={run.status !== "Succeeded"}
                >
                  {run.fileName} · {new Date(run.creationTime).toLocaleString()}{" "}
                  · {run.status}
                </option>
              ))}
            </select>
          </label>
          <div className="flex flex-wrap items-center gap-2">
            <input
              type="file"
              className={`${inputClass} min-w-0 flex-1`}
              onChange={(event) => setFile(event.target.files?.[0] ?? null)}
            />
            <Button
              type="button"
              variant="outline"
              disabled={!file || !parser?.isActive || pending}
              onClick={() => void upload()}
            >
              {pending ? "Parsing…" : "Upload & parse"}
            </Button>
          </div>
          {!parser?.isActive ? (
            <p className="text-sm text-muted-foreground">
              Publish this parser before uploading a new file.
            </p>
          ) : null}
          {pending ? (
            <p className="text-sm text-muted-foreground">
              Waiting for the parser worker to finish.
            </p>
          ) : null}
          {(runs.data ?? [])
            .filter((run) => run.status === "Failed")
            .slice(0, 1)
            .map((run) => (
              <p key={run.id} className="text-xs text-destructive">
                {run.fileName}: {run.error || "Parsing failed."}
              </p>
            ))}
        </>
      )}
      {error || runs.error ? (
        <ErrorNote message={error || (runs.error as Error).message} />
      ) : null}
    </fieldset>
  )
}
