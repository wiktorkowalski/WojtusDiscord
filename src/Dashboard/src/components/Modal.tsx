import type { ReactNode } from 'react'

interface ModalProps {
  title: string
  onClose: () => void
  children: ReactNode
}

export default function Modal({ title, onClose, children }: ModalProps) {
  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
      onClick={onClose}
    >
      <div
        className="flex max-h-[85vh] w-full max-w-3xl flex-col overflow-hidden rounded-xl border border-discord-border bg-discord-bg-alt shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between border-b border-discord-border px-4 py-3">
          <h3 className="font-semibold text-white">{title}</h3>
          <button onClick={onClose} className="text-discord-muted hover:text-white">
            ✕
          </button>
        </div>
        <div className="overflow-auto p-4">{children}</div>
      </div>
    </div>
  )
}
