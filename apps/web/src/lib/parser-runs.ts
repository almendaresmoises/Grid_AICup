import { api } from "@/lib/api"
import type { ParserRun } from "@/lib/canonical"

export async function uploadAndParse(
  parserId: string,
  file: File,
  signal?: AbortSignal
): Promise<ParserRun> {
  const body = new FormData()
  body.append("file", file)
  let run = await api<ParserRun>(`/api/parsers/${parserId}/runs`, {
    method: "POST",
    body,
    signal,
  })
  const deadline = Date.now() + 10 * 60_000
  while (run.status === "Queued") {
    if (Date.now() > deadline)
      throw new Error(
        "Parsing is still queued. Check that the parser worker is running, then select the completed file from the list."
      )
    await new Promise<void>((resolve, reject) => {
      const timer = setTimeout(() => {
        signal?.removeEventListener("abort", abort)
        resolve()
      }, 2000)
      function abort() {
        clearTimeout(timer)
        reject(new DOMException("Cancelled", "AbortError"))
      }
      if (signal?.aborted) abort()
      else signal?.addEventListener("abort", abort, { once: true })
    })
    const runs = await api<ParserRun[]>(`/api/parsers/${parserId}/runs`, {
      signal,
    })
    run = runs.find((item) => item.id === run.id) ?? run
  }
  if (run.status !== "Succeeded")
    throw new Error(
      run.error || "Parsing failed. Review the parser configuration."
    )
  return run
}
