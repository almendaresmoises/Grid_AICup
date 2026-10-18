import { useQuery } from "@tanstack/react-query"
import { Button } from "@workspace/ui/components/button"
import { useState } from "react"
import { useNavigate } from "react-router"
import { api } from "@/lib/api"
import type { ConfigurationValidation } from "@/lib/ai-configuration"

export function ConfigurationReadiness({
  id,
  unsaved = false,
}: {
  id: string
  unsaved?: boolean
}) {
  const navigate = useNavigate()
  const [error, setError] = useState("")
  const [running, setRunning] = useState(false)
  const validation = useQuery({
    queryKey: ["configuration-validation", id],
    queryFn: () =>
      api<ConfigurationValidation>(
        `/api/app/ai-configuration/${id}/validation`
      ),
    staleTime: 0,
  })
  const report = validation.data
  async function run() {
    if (!report?.passed) return
    setRunning(true)
    setError("")
    try {
      const result = await api<{ id: string }>(
        `/api/app/ai-configuration/${id}/run`,
        {
          method: "POST",
          body: JSON.stringify({ revision: report.revision }),
        }
      )
      navigate(`/reconciliation/runs/${result.id}/grid`)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Run failed.")
      void validation.refetch()
    } finally {
      setRunning(false)
    }
  }
  return (
    <div className="flex flex-col gap-3 rounded border bg-muted/20 p-4">
      <p className="font-medium">Validation on uploaded files</p>
      <p className="text-sm text-muted-foreground">
        Uses every file assigned to each dataset and the current saved profile.
        Running approves this validated revision.
      </p>
      {unsaved && (
        <p className="text-sm text-amber-700 dark:text-amber-400">
          Save your changes before validating and running.
        </p>
      )}
      {validation.isFetching && (
        <p className="text-sm">
          Checking canonical outputs and previewing reconciliation…
        </p>
      )}
      {report && (
        <>
          <p className="text-sm">
            {report.totalRecords.toLocaleString()} records:{" "}
            {report.fullMatchRecords.toLocaleString()} full matches,{" "}
            {report.partialMatchRecords.toLocaleString()} partial matches,{" "}
            {report.exceptionRecords.toLocaleString()} exceptions.
          </p>
          {report.passes.length > 0 && (
            <ul className="text-sm text-muted-foreground">
              {report.passes.map((pass) => (
                <li key={pass.rule}>
                  {pass.rule}: {pass.groups.toLocaleString()} groups /{" "}
                  {pass.records.toLocaleString()} records
                </li>
              ))}
            </ul>
          )}
          {report.errors.map((message) => (
            <p key={message} role="alert" className="text-sm text-destructive">
              {message}
            </p>
          ))}
          {report.warnings.length > 0 && (
            <details className="text-sm">
              <summary>{report.warnings.length} validation notes</summary>
              <ul className="list-disc pl-5">
                {report.warnings.map((warning, index) => (
                  <li key={`${index}-${warning}`}>{warning}</li>
                ))}
              </ul>
            </details>
          )}
          {report.columns.length > 0 && (
            <details className="text-sm">
              <summary>Parsed column quality</summary>
              <div className="mt-2 overflow-x-auto">
                <table className="w-full text-left text-xs">
                  <thead>
                    <tr>
                      <th>Dataset</th>
                      <th>Column</th>
                      <th>Populated</th>
                      <th>Distinct values</th>
                    </tr>
                  </thead>
                  <tbody>
                    {report.columns.map((column) => (
                      <tr key={`${column.dataset}-${column.column}`}>
                        <td>{column.dataset}</td>
                        <td>{column.column}</td>
                        <td>
                          {(column.total - column.missing).toLocaleString()} /{" "}
                          {column.total.toLocaleString()}
                        </td>
                        <td>{column.distinct.toLocaleString()}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </details>
          )}
        </>
      )}
      {(error || validation.error) && (
        <p role="alert" className="text-sm text-destructive">
          {error || validation.error?.message}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <Button
          variant="outline"
          disabled={unsaved || validation.isFetching || running}
          onClick={() => void validation.refetch()}
        >
          Refresh validation
        </Button>
        <Button
          disabled={
            unsaved || !report?.passed || validation.isFetching || running
          }
          onClick={() => void run()}
        >
          {running ? "Running…" : "Run uploaded files"}
        </Button>
      </div>
    </div>
  )
}
