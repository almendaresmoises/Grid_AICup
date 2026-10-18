import { Button } from "@workspace/ui/components/button"
import { useState } from "react"
import { useNavigate } from "react-router"
import { ApiError, login } from "@/lib/api"
import { ErrorNote } from "@/components/error-note"
import { inputClass, labelClass } from "@/lib/ui"
import { Eye, EyeOff, Loader2 } from "lucide-react"
import { RgLogo } from "@/components/rg-logo"

export function LoginPage() {
  const navigate = useNavigate()
  const [username, setUsername] = useState("admin")
  const [password, setPassword] = useState("")
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState("")
  const [pending, setPending] = useState(false)

  return (
    <div className="flex min-h-svh items-center justify-center bg-muted/20 px-4 py-12">
      <form
        className="flex w-full max-w-sm flex-col gap-4 rounded-xl border bg-card p-7 shadow-xs"
        onSubmit={async (event) => {
          event.preventDefault()
          setPending(true)
          setError("")
          try {
            await login(username, password)
            navigate("/")
          } catch (caught) {
            setError(caught instanceof ApiError ? caught.message : "Sign-in failed")
          } finally {
            setPending(false)
          }
        }}
      >
        <div className="flex flex-col items-center text-center">
          <div className="mb-3 inline-flex items-center justify-center">
            <RgLogo size="xl" />
          </div>
          <h1 className="text-2xl font-semibold tracking-tight font-heading">ReconGrid</h1>
          <p className="text-muted-foreground mt-1 text-sm">

          </p>
        </div>
        {error ? <ErrorNote message={error} /> : null}
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Username</span>
          <input className={inputClass} value={username} onChange={(event) => setUsername(event.target.value)} />
        </label>
        <label className="flex flex-col gap-1.5">
          <span className={labelClass}>Password</span>
          <div className="relative flex items-center">
            <input
              className={`${inputClass} pr-10`}
              type={showPassword ? "text" : "password"}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              placeholder="Enter password"
              autoComplete="current-password"
            />
            <button
              type="button"
              onClick={() => setShowPassword((prev) => !prev)}
              className="absolute right-2 inline-flex size-7 items-center justify-center rounded-sm text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
              aria-label={showPassword ? "Hide password" : "Show password"}
              title={showPassword ? "Hide password" : "Show password"}
              tabIndex={-1}
            >
              {showPassword ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
            </button>
          </div>
        </label>
        <Button type="submit" disabled={pending} className="w-full">
          {pending ? (
            <>
              <Loader2 className="mr-2 size-4 animate-spin" />
              Signing in…
            </>
          ) : (
            "Sign in"
          )}
        </Button>
      </form>
    </div>
  )
}
