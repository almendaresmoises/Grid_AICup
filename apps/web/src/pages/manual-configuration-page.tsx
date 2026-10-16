import { Button } from "@workspace/ui/components/button"
import { NavLink, Outlet, Link } from "react-router"

const tabs = [
  { to: "/configuration/manual/parsers", label: "Parsers" },
  { to: "/configuration/manual/datasets", label: "Datasets" },
  { to: "/configuration/manual/profiles", label: "Recon Profiles" },
]

export function ManualConfigurationPage() {
  return (
    <section className="flex flex-col gap-6">
      <div>
        <Button variant="link" className="h-auto px-0" render={<Link to="/configuration" />}>
          Configuration
        </Button>
        <h1 className="text-2xl font-medium">Manually Configure</h1>
        <p className="text-muted-foreground mt-1 text-sm">Edit parsers, datasets, and recon profiles on their own tabs.</p>
      </div>
      <div className="flex gap-1 border-b" role="tablist" aria-label="Manual configuration">
        {tabs.map((tab) => (
          <NavLink
            key={tab.to}
            to={tab.to}
            role="tab"
            className={({ isActive }) =>
              `px-3 py-2 text-sm ${isActive ? "border-b-2 border-primary font-medium" : "text-muted-foreground hover:text-foreground"}`
            }
          >
            {tab.label}
          </NavLink>
        ))}
      </div>
      <Outlet />
    </section>
  )
}
