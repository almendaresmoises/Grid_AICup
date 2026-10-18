import { Sparkles, SlidersHorizontal, Loader2, CheckCircle2, Clock } from "lucide-react"
import { useEffect, useState } from "react"
import { Link } from "react-router"
import { getAiSessionState, subscribeAiSession, type AiSessionState } from "@/lib/ai-config-session"

export function ConfigurationPage() {
  const [session, setSession] = useState<AiSessionState>(getAiSessionState)

  useEffect(() => {
    return subscribeAiSession(setSession)
  }, [])

  return (
    <section className="flex flex-col gap-6">
      <div>
        <h1 className="text-2xl font-medium">Configuration</h1>
        <p className="text-muted-foreground mt-1 text-sm">Choose how you want to set up reconciliation.</p>
      </div>
      <div className="grid gap-4 sm:grid-cols-2">
        <Link
          to="/configuration/ai"
          className="relative flex flex-col gap-3 rounded-lg border bg-card p-5 shadow-sm transition-colors hover:border-primary/50 hover:bg-muted focus-visible:border-primary focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        >
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="flex items-center gap-2">
              <Sparkles className="size-5 text-primary" />
              <span className="rounded-full border border-primary/20 bg-primary/10 px-2 py-0.5 text-xs font-medium text-primary">
                Recommended
              </span>
            </div>
            {session.status === "running" ? (
              <span className="flex items-center gap-1.5 rounded-full bg-primary/10 px-2 py-0.5 text-xs font-semibold text-primary animate-pulse">
                <Loader2 className="size-3 animate-spin" />
                Step {session.currentStep} of 6
              </span>
            ) : session.status === "paused" ? (
              <span className="flex items-center gap-1.5 rounded-full bg-amber-500/10 px-2 py-0.5 text-xs font-semibold text-amber-600 dark:text-amber-400">
                <Clock className="size-3" />
                Paused on reload
              </span>
            ) : session.status === "completed" ? (
              <span className="flex items-center gap-1.5 rounded-full bg-emerald-500/10 px-2 py-0.5 text-xs font-semibold text-emerald-600 dark:text-emerald-400">
                <CheckCircle2 className="size-3" />
                Ready
              </span>
            ) : null}
          </div>
          <div>
            <h2 className="font-medium text-base">Configure with AI</h2>
            <p className="text-muted-foreground mt-1 text-sm">
              {session.status === "running"
                ? `Currently ongoing: ${session.stepMessage || "Processing files…"}`
                : session.status === "completed"
                ? "Setup completed. Review configuration or run samples."
                : "Upload one file per dataset and let the assistant draft a profile."}
            </p>
          </div>
          <p className="mt-auto pt-1 text-xs text-muted-foreground">
            Best for first-time setup
          </p>
        </Link>

        <Link
          to="/configuration/manual"
          className="flex flex-col gap-3 rounded-lg border bg-card p-5 shadow-sm transition-colors hover:border-primary/50 hover:bg-muted focus-visible:border-primary focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        >
          <div className="flex items-center justify-between">
            <SlidersHorizontal className="size-5 text-primary" />
          </div>
          <div>
            <h2 className="font-medium text-base">Manually Configure</h2>
            <p className="text-muted-foreground mt-1 text-sm">
              Edit parsers, datasets, and recon profiles yourself.
            </p>
          </div>
          <p className="mt-auto pt-1 text-xs text-muted-foreground">
            For advanced tuning
          </p>
        </Link>
      </div>
    </section>
  )
}
