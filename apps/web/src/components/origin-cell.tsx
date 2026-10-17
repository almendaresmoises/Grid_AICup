import { Sparkles } from "lucide-react"

export function OriginCell({ createdWithAi }: { createdWithAi?: boolean }) {
  return (
    <span className="inline-flex items-center gap-1.5">
      {createdWithAi ? <Sparkles className="size-3.5 text-primary" /> : null}
      {createdWithAi ? "AI" : "Manual"}
    </span>
  )
}
