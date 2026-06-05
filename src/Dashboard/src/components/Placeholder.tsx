// Temporary stub for views landing in later slices. Keeps routing/nav wired
// end-to-end during the tracer-bullet phase.
export default function Placeholder({ title }: { title: string }) {
  return (
    <div className="p-8">
      <h2 className="text-2xl font-semibold text-white">{title}</h2>
      <p className="mt-2 text-discord-muted">Coming soon.</p>
    </div>
  )
}
