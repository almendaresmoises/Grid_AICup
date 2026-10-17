import { useQuery, useQueryClient } from "@tanstack/react-query"
import { ChevronLeft, LayoutDashboard, Play, Settings2, LogOut } from "lucide-react"
import { useEffect, useState } from "react"
import { NavLink, Outlet, useNavigate } from "react-router"
import { api, clearToken } from "@/lib/api"
import { getAiSessionState, subscribeAiSession, type AiSessionState } from "@/lib/ai-config-session"

const storageKey = "grid_sidebar"

const links = [
  { to: "/", label: "Dashboard", icon: LayoutDashboard, end: true },
  { to: "/reconciliation/runs", label: "Reconciliation Runs", icon: Play, end: false },
  { to: "/configuration", label: "Configuration", icon: Settings2, end: false },
]

const ease = "ease-[cubic-bezier(0.22,1,0.36,1)]"

function labelMotion(collapsed: boolean) {
  return `overflow-hidden whitespace-nowrap transition-[max-width,opacity] motion-reduce:transition-none ${ease} ${
    collapsed ? "max-w-0 opacity-0 duration-150" : "max-w-44 opacity-100 delay-100 duration-200"
  }`
}

type CurrentUser = {
  isAuthenticated: boolean
  userName?: string | null
  name?: string | null
  surName?: string | null
  email?: string | null
  roles?: string[] | null
}

function formatRole(role: string) {
  return role.charAt(0).toUpperCase() + role.slice(1)
}

function initials(value: string) {
  const parts = value.split(/\s+/).filter(Boolean)
  const letters = parts.length > 1 ? `${parts[0]?.[0] ?? ""}${parts[1]?.[0] ?? ""}` : value.slice(0, 2)
  return letters.toUpperCase()
}

function accountLabel(user: CurrentUser) {
  const fullName = [user.name, user.surName].filter(Boolean).join(" ").trim()
  const account = user.userName || user.email || "Signed in"
  const named = fullName.length > 0 && fullName.toLowerCase() !== account.toLowerCase()
  const roles = (user.roles ?? []).map(formatRole).join(", ")
  return {
    primary: named ? fullName : account,
    secondary: named ? [account, roles].filter(Boolean).join(" · ") : roles,
  }
}

export function Shell() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem(storageKey) === "collapsed")
  const currentUser = useQuery({
    queryKey: ["current-user"],
    queryFn: async () => {
      const config = await api<{ currentUser: CurrentUser }>("/api/abp/application-configuration")
      return config.currentUser
    },
    staleTime: 5 * 60 * 1000,
    retry: 1,
  })
  const account = currentUser.data?.isAuthenticated ? accountLabel(currentUser.data) : null

  const [session, setSession] = useState<AiSessionState>(getAiSessionState)

  useEffect(() => {
    localStorage.setItem(storageKey, collapsed ? "collapsed" : "expanded")
  }, [collapsed])

  useEffect(() => {
    return subscribeAiSession(setSession)
  }, [])

  return (
    <div className="flex h-svh overflow-hidden bg-background text-foreground">
      <div className="relative flex h-full shrink-0">
        <aside
          id="app-sidebar"
          className={`flex h-full flex-col overflow-hidden bg-sidebar text-sidebar-foreground shadow-[8px_0_24px_-16px_rgba(0,0,0,0.55)] transition-[width] duration-300 motion-reduce:transition-none ${ease} ${collapsed ? "w-14" : "w-60"}`}
        >
          <div className="flex h-14 items-center border-b border-sidebar-border px-5">
            <p className={`text-sm font-medium tracking-wide ${labelMotion(collapsed)}`}>GRID</p>
          </div>
          <nav className="flex flex-1 flex-col gap-1 p-2" aria-label="Main">
            {links.map((link) => {
              const Icon = link.icon
              const isConfigRunning = link.to === "/configuration" && session.status === "running"

              return (
                <NavLink
                  key={link.to}
                  to={link.to}
                  end={link.end}
                  title={collapsed ? link.label : undefined}
                  className={({ isActive }) =>
                    `flex h-9 items-center gap-3 px-3 text-sm relative ${
                      isActive
                        ? "bg-sidebar-accent font-medium text-sidebar-accent-foreground"
                        : "text-sidebar-foreground/70 hover:bg-sidebar-accent hover:text-sidebar-accent-foreground"
                    }`
                  }
                >
                  <Icon className="size-4 shrink-0" />
                  <span className={labelMotion(collapsed)}>{link.label}</span>
                  {isConfigRunning ? (
                    <span
                      title="AI Configuration is currently running in background"
                      className="ml-auto size-2 rounded-full bg-primary animate-ping"
                    />
                  ) : null}
                </NavLink>
              )
            })}
          </nav>
          <div className="border-t border-sidebar-border p-2">
            {account ? (
              <div
                className="flex h-12 items-center gap-3 px-1"
                title={[account.primary, account.secondary].filter(Boolean).join(" · ")}
              >
                <span className="flex size-8 shrink-0 items-center justify-center bg-sidebar-accent text-xs font-medium">
                  {initials(account.primary)}
                </span>
                <span className={`min-w-0 ${labelMotion(collapsed)}`}>
                  <span className="block truncate text-sm font-medium leading-tight">{account.primary}</span>
                  {account.secondary ? (
                    <span className="block truncate text-xs leading-tight text-sidebar-foreground/70">{account.secondary}</span>
                  ) : null}
                </span>
              </div>
            ) : null}
            <button
              type="button"
              title={collapsed ? "Sign out" : undefined}
              className="flex h-9 w-full items-center gap-3 px-3 text-sm text-sidebar-foreground/70 hover:bg-sidebar-accent hover:text-sidebar-accent-foreground"
              onClick={() => {
                clearToken()
                queryClient.removeQueries({ queryKey: ["current-user"] })
                navigate("/login")
              }}
            >
              <LogOut className="size-4 shrink-0" />
              <span className={labelMotion(collapsed)}>Sign out</span>
            </button>
          </div>
        </aside>
        <button
          type="button"
          className="absolute top-7 right-0 z-10 flex size-7 translate-x-1/2 -translate-y-1/2 items-center justify-center rounded-full border-0 bg-[color-mix(in_oklch,white_34%,var(--sidebar))] text-[color-mix(in_oklch,black_42%,var(--sidebar))] shadow-[0_1px_2px_rgba(0,0,0,0.16),0_6px_14px_-6px_rgba(0,0,0,0.4)] transition-colors hover:bg-[color-mix(in_oklch,white_46%,var(--sidebar))]"
          aria-expanded={!collapsed}
          aria-controls="app-sidebar"
          aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
          onClick={() => setCollapsed((current) => !current)}
        >
          <ChevronLeft
            className={`size-4 transition-transform duration-300 motion-reduce:transition-none ${ease} ${collapsed ? "rotate-180" : ""}`}
          />
        </button>
      </div>
      <main className="min-w-0 flex-1 overflow-auto">
        <div className="mx-auto flex w-full max-w-5xl flex-col gap-6 px-8 py-8">
          <Outlet />
        </div>
      </main>
    </div>
  )
}
