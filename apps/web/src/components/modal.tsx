import { useEffect, useState, type ReactNode } from "react"
import { createPortal } from "react-dom"
import { Maximize2, Minimize2, X } from "lucide-react"
import { Button } from "@workspace/ui/components/button"
import { cn } from "cn"

export interface ModalProps {
  isOpen: boolean
  onClose: () => void
  title: ReactNode
  description?: ReactNode
  children: ReactNode
  footer?: ReactNode
  size?: "sm" | "md" | "lg" | "xl" | "2xl" | "3xl" | "4xl" | "5xl" | "full"
  defaultFullScreen?: boolean
  closeOnBackdropClick?: boolean
  closeOnEscape?: boolean
  className?: string
  contentClassName?: string
}

const sizeClasses: Record<NonNullable<ModalProps["size"]>, string> = {
  sm: "max-w-md",
  md: "max-w-lg",
  lg: "max-w-2xl",
  xl: "max-w-3xl",
  "2xl": "max-w-4xl",
  "3xl": "max-w-5xl",
  "4xl": "max-w-6xl",
  "5xl": "max-w-7xl",
  full: "max-w-[96vw]",
}

export function Modal(props: ModalProps) {
  if (!props.isOpen || typeof document === "undefined") {
    return null
  }
  return createPortal(<ModalDialog {...props} />, document.body)
}

function ModalDialog({
  onClose,
  title,
  description,
  children,
  footer,
  size = "2xl",
  defaultFullScreen = false,
  closeOnBackdropClick = true,
  closeOnEscape = true,
  className,
  contentClassName,
}: ModalProps) {
  const [isFullScreen, setIsFullScreen] = useState(defaultFullScreen)

  // Lock body scroll while modal is mounted
  useEffect(() => {
    const originalStyle = document.body.style.overflow
    document.body.style.overflow = "hidden"
    return () => {
      document.body.style.overflow = originalStyle
    }
  }, [])

  // Handle Escape key
  useEffect(() => {
    if (!closeOnEscape) return
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.stopPropagation()
        onClose()
      }
    }
    window.addEventListener("keydown", handleKeyDown)
    return () => window.removeEventListener("keydown", handleKeyDown)
  }, [closeOnEscape, onClose])

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-0 md:p-4"
      role="dialog"
      aria-modal="true"
    >
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-background/80 backdrop-blur-xs transition-opacity duration-150"
        onClick={closeOnBackdropClick ? onClose : undefined}
        aria-hidden="true"
      />

      {/* Modal Dialog Container */}
      <div
        className={cn(
          "relative flex flex-col bg-card border text-card-foreground shadow-2xl transition-all duration-150 overflow-hidden",
          isFullScreen
            ? "fixed inset-0 z-50 w-screen h-screen max-w-none max-h-none rounded-none border-0"
            : cn("w-full max-h-[92vh] rounded-lg", sizeClasses[size]),
          className
        )}
      >
        {/* Header */}
        <div className="flex items-start justify-between gap-4 border-b px-5 py-4 shrink-0 bg-card/95">
          <div className="min-w-0 flex-1">
            <h2 className="text-base font-semibold leading-tight text-foreground truncate">
              {title}
            </h2>
            {description ? (
              <p className="mt-1 text-xs text-muted-foreground">{description}</p>
            ) : null}
          </div>
          <div className="flex items-center gap-1 shrink-0">
            <Button
              type="button"
              variant="ghost"
              size="icon-sm"
              aria-label={isFullScreen ? "Exit full screen" : "Full screen"}
              title={isFullScreen ? "Exit full screen" : "Full screen"}
              onClick={() => setIsFullScreen((current) => !current)}
            >
              {isFullScreen ? (
                <Minimize2 className="size-4" />
              ) : (
                <Maximize2 className="size-4" />
              )}
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="icon-sm"
              aria-label="Close"
              title="Close"
              onClick={onClose}
            >
              <X className="size-4" />
            </Button>
          </div>
        </div>

        {/* Scrollable Content Body */}
        <div
          className={cn(
            "flex-1 overflow-y-auto p-5 text-foreground",
            contentClassName
          )}
        >
          {children}
        </div>

        {/* Optional Footer */}
        {footer ? (
          <div className="border-t bg-muted/20 px-5 py-3 shrink-0 flex items-center justify-end gap-2">
            {footer}
          </div>
        ) : null}
      </div>
    </div>
  )
}
