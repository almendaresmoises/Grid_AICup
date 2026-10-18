import type { HTMLAttributes } from "react"

interface RgLogoProps extends HTMLAttributes<HTMLDivElement> {
  size?: "sm" | "md" | "lg" | "xl"
  variant?: "primary" | "sidebar" | "outline"
  showText?: boolean
  textClassName?: string
}

export function RgLogo({
  size = "md",
  variant = "primary",
  showText = false,
  textClassName = "",
  className = "",
  ...props
}: RgLogoProps) {
  const sizeMap = {
    sm: "size-6 text-xs rounded-md",
    md: "size-8 text-sm rounded-lg",
    lg: "size-10 text-base rounded-lg",
    xl: "size-12 text-xl rounded-xl",
  }

  const variantMap = {
    primary: "bg-primary text-primary-foreground shadow-xs ring-1 ring-primary/20",
    sidebar: "bg-sidebar-primary text-sidebar-primary-foreground shadow-xs",
    outline: "border border-border bg-background text-foreground shadow-xs",
  }

  return (
    <div className={`inline-flex items-center gap-2.5 ${className}`} {...props}>
      <div
        className={`${sizeMap[size]} ${variantMap[variant]} shrink-0 flex items-center justify-center font-heading font-black tracking-tighter select-none`}
        style={{ fontFamily: "'Figtree Variable', 'Figtree', sans-serif" }}
        aria-label="ReconGrid Logo"
      >
        RG
      </div>
      {showText && (
        <span
          className={`font-heading font-bold tracking-tight ${textClassName}`}
          style={{ fontFamily: "'Figtree Variable', 'Figtree', sans-serif" }}
        >
          ReconGrid
        </span>
      )}
    </div>
  )
}
