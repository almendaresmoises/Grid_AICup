import { Button } from "@workspace/ui/components/button"
import { useState } from "react"
import { useNavigate } from "react-router"
import { ApiError, login } from "@/lib/api"
import { ErrorNote } from "@/components/error-note"
import { inputClass, labelClass } from "@/lib/ui"

export function LoginPage() {
  const navigate = useNavigate()
  const [username, setUsername] = useState("admin")
  const [password, setPassword] = useState("")
  const [error, setError] = useState("")
  const [pending, setPending] = useState(false)

  return (
    <div className="flex min-h-svh items-center justify-center bg-background px-6">
      <form
        className="flex w-full max-w-sm flex-col gap-4 border bg-card p-6"
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
        <div>
          <h1 className="text-xl font-medium">ReconGrid</h1>
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
          <input
            className={inputClass}
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </label>
        <Button type="submit" disabled={pending}>
          {pending ? "Signing in…" : "Sign in"}
        </Button>
      </form>
    </div>
  )
}
