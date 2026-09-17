interface CentrixLogoProps {
  className?: string;
  size?: number;
}

export function CentrixLogo({ className = 'w-7 h-7', size = 28 }: CentrixLogoProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 512 512"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={`shrink-0 transition-transform duration-300 hover:scale-105 ${className}`}
      aria-label="Centrix Logo"
    >
      <defs>
        <linearGradient id="centrixBlueGrad" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="#0066FF" />
          <stop offset="100%" stopColor="#0047BA" />
        </linearGradient>
      </defs>

      {/* Top-Right Quadrant */}
      <g>
        <path d="M 264 12 A 244 244 0 0 1 500 248 L 264 248 Z" fill="url(#centrixBlueGrad)" />
        <path d="M 264 116 C 340 144 424 248 424 248 C 344 228 276 182 264 116 Z" fill="#FFFFFF" />
      </g>

      {/* Bottom-Right Quadrant */}
      <g>
        <path d="M 500 264 A 244 244 0 0 1 264 500 L 264 264 Z" fill="url(#centrixBlueGrad)" />
        <path d="M 396 264 C 368 340 264 424 264 424 C 284 344 330 276 396 264 Z" fill="#FFFFFF" />
      </g>

      {/* Bottom-Left Quadrant */}
      <g>
        <path d="M 248 500 A 244 244 0 0 1 12 264 L 248 264 Z" fill="url(#centrixBlueGrad)" />
        <path d="M 248 396 C 172 368 88 264 88 264 C 168 284 236 330 248 396 Z" fill="#FFFFFF" />
      </g>

      {/* Top-Left Quadrant */}
      <g>
        <path d="M 12 248 A 244 244 0 0 1 248 12 L 248 248 Z" fill="url(#centrixBlueGrad)" />
        <path d="M 116 248 C 144 172 248 88 248 88 C 228 168 182 236 116 248 Z" fill="#FFFFFF" />
      </g>
    </svg>
  );
}

export default CentrixLogo;
