import { Sparkles, SlidersHorizontal } from "lucide-react"
import { Link } from "react-router"

const options = [
  {
    to: "/configuration/ai",
    title: "Configure with AI",
    detail: "Upload one file per dataset and let the assistant draft a profile.",
    icon: Sparkles,
  },
  {
    to: "/configuration/manual",
    title: "Manually Configure",
    detail: "Edit parsers, datasets, and recon profiles yourself.",
    icon: SlidersHorizontal,
  },
]

export function ConfigurationPage() {
  return (
    <section className="flex flex-col gap-6">
      <div>
        <h1 className="text-2xl font-medium">Configuration</h1>
        <p className="text-muted-foreground mt-1 text-sm">Choose how you want to set up reconciliation.</p>
      </div>
      <div className="grid gap-4 sm:grid-cols-2">
        {options.map((option) => {
          const Icon = option.icon
          return (
            <Link key={option.to} to={option.to} className="flex flex-col gap-3 border bg-card p-5 hover:bg-muted">
              <Icon className="size-5 text-primary" />
              <div>
                <h2 className="font-medium">{option.title}</h2>
                <p className="text-muted-foreground mt-1 text-sm">{option.detail}</p>
              </div>
            </Link>
          )
        })}
      </div>
    </section>
  )
}
