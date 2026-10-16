export function ErrorNote({ message }: { message: string }) {
  return (
    <p className="border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">
      {message}
    </p>
  )
}
