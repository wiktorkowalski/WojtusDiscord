import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { entitiesApi } from '../api/entitiesApi'
import Pagination from '../components/Pagination'
import Modal from '../components/Modal'
import UserChip from '../components/UserChip'
import ChannelChip from '../components/ChannelChip'
import { ErrorState, Loading, PageHeader } from '../components/StateBlocks'
import { formatTimestamp, relativeTime, truncate } from '../utils/format'

type Tab = 'users' | 'channels' | 'members' | 'messages'
const TABS: { key: Tab; label: string }[] = [
  { key: 'users', label: 'Users' },
  { key: 'channels', label: 'Channels' },
  { key: 'members', label: 'Members' },
  { key: 'messages', label: 'Messages' },
]

export default function Entities() {
  const [tab, setTab] = useState<Tab>('users')

  return (
    <div className="p-8">
      <PageHeader title="Entities" subtitle="Current-state core data" />

      <div className="mb-5 flex gap-1 border-b border-discord-border">
        {TABS.map((t) => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            className={`-mb-px border-b-2 px-4 py-2 text-sm font-medium ${
              tab === t.key
                ? 'border-blurple text-white'
                : 'border-transparent text-discord-muted hover:text-white'
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'users' && <UsersTab />}
      {tab === 'channels' && <ChannelsTab />}
      {tab === 'members' && <MembersTab />}
      {tab === 'messages' && <MessagesTab />}
    </div>
  )
}

function Th({ children }: { children: React.ReactNode }) {
  return <th className="px-3 py-2 text-left font-semibold text-discord-muted">{children}</th>
}

function TableShell({
  head,
  children,
}: {
  head: React.ReactNode
  children: React.ReactNode
}) {
  return (
    <div className="overflow-x-auto rounded-lg border border-discord-border">
      <table className="w-full text-sm">
        <thead className="bg-discord-bg-alt">
          <tr>{head}</tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  )
}

function UsersTab() {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [search, setSearch] = useState('')
  const [applied, setApplied] = useState('')
  const [selected, setSelected] = useState<string | null>(null)

  const list = useQuery({
    queryKey: ['users', page, pageSize, applied],
    queryFn: () => entitiesApi.users({ page, pageSize, search: applied || undefined }),
    placeholderData: keepPreviousData,
  })
  const detail = useQuery({
    queryKey: ['user', selected],
    queryFn: () => entitiesApi.user(selected!),
    enabled: selected !== null,
  })

  if (list.isLoading) return <Loading />
  if (list.isError) return <ErrorState error={list.error} />

  return (
    <>
      <div className="mb-3 flex gap-2">
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && (setApplied(search), setPage(1))}
          placeholder="search username / global name"
          className="w-72 rounded bg-discord-bg-dark px-3 py-1.5 text-sm text-discord-text"
        />
        <button
          onClick={() => {
            setApplied(search)
            setPage(1)
          }}
          className="rounded bg-blurple px-3 py-1.5 text-sm text-white hover:bg-blurple-hover"
        >
          Search
        </button>
      </div>
      <TableShell
        head={
          <>
            <Th>User</Th>
            <Th>Global name</Th>
            <Th>Bot</Th>
            <Th>First seen</Th>
          </>
        }
      >
        {list.data!.items.map((u) => (
          <tr
            key={u.id}
            onClick={() => setSelected(u.id)}
            className="cursor-pointer border-b border-discord-border/40 hover:bg-discord-bg-card"
          >
            <td className="px-3 py-1.5">
              <UserChip name={u.username} id={u.discordId} />
            </td>
            <td className="px-3 py-1.5 text-discord-muted">{u.globalName ?? '—'}</td>
            <td className="px-3 py-1.5">{u.isBot ? '🤖' : ''}</td>
            <td className="px-3 py-1.5 text-discord-faint" title={formatTimestamp(u.firstSeenUtc).title}>
              {formatTimestamp(u.firstSeenUtc).text}
            </td>
          </tr>
        ))}
      </TableShell>
      <div className="mt-4">
        <Pagination page={page} pageSize={pageSize} total={list.data!.totalCount} onPageChange={setPage} onPageSizeChange={(s) => (setPageSize(s), setPage(1))} />
      </div>

      {selected && (
        <Modal title="User" onClose={() => setSelected(null)}>
          {detail.isLoading && <Loading />}
          {detail.data && (
            <div className="space-y-4">
              <UserChip name={detail.data.username} id={detail.data.discordId} />
              <dl className="grid grid-cols-2 gap-2 text-sm">
                <Field k="Global name" v={detail.data.globalName ?? '—'} />
                <Field k="Memberships" v={String(detail.data.membershipCount)} />
                <Field k="Bot" v={detail.data.isBot ? 'yes' : 'no'} />
                <Field k="First seen" v={formatTimestamp(detail.data.firstSeenUtc).text} />
              </dl>
              <div>
                <h4 className="mb-2 text-sm font-semibold text-white">Name history</h4>
                {detail.data.nameHistory.length === 0 ? (
                  <p className="text-sm text-discord-faint">No recorded changes.</p>
                ) : (
                  <ul className="space-y-1 text-sm">
                    {detail.data.nameHistory.map((h, i) => (
                      <li key={i} className="text-discord-muted">
                        <span className="text-discord-faint">{relativeTime(h.changedAtUtc)}: </span>
                        {h.usernameBefore ?? '∅'} → {h.usernameAfter ?? '∅'}
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </div>
          )}
        </Modal>
      )}
    </>
  )
}

function ChannelsTab() {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [selected, setSelected] = useState<string | null>(null)

  const list = useQuery({
    queryKey: ['channels', page, pageSize],
    queryFn: () => entitiesApi.channels({ page, pageSize }),
    placeholderData: keepPreviousData,
  })
  const detail = useQuery({
    queryKey: ['channel', selected],
    queryFn: () => entitiesApi.channel(selected!),
    enabled: selected !== null,
  })

  if (list.isLoading) return <Loading />
  if (list.isError) return <ErrorState error={list.error} />

  return (
    <>
      <TableShell
        head={
          <>
            <Th>Channel</Th>
            <Th>Type</Th>
            <Th>Position</Th>
            <Th>State</Th>
          </>
        }
      >
        {list.data!.items.map((c) => (
          <tr
            key={c.id}
            onClick={() => setSelected(c.id)}
            className="cursor-pointer border-b border-discord-border/40 hover:bg-discord-bg-card"
          >
            <td className="px-3 py-1.5">
              <ChannelChip name={c.name} id={c.discordId} />
            </td>
            <td className="px-3 py-1.5 text-discord-muted">{c.type}</td>
            <td className="px-3 py-1.5 tabular-nums text-discord-faint">{c.position}</td>
            <td className="px-3 py-1.5">
              {c.isDeleted ? <span className="text-discord-red">deleted</span> : <span className="text-discord-green">active</span>}
            </td>
          </tr>
        ))}
      </TableShell>
      <div className="mt-4">
        <Pagination page={page} pageSize={pageSize} total={list.data!.totalCount} onPageChange={setPage} onPageSizeChange={(s) => (setPageSize(s), setPage(1))} />
      </div>

      {selected && (
        <Modal title="Channel" onClose={() => setSelected(null)}>
          {detail.isLoading && <Loading />}
          {detail.data && (
            <div className="space-y-3">
              <ChannelChip name={detail.data.name} id={detail.data.discordId} />
              <dl className="grid grid-cols-2 gap-2 text-sm">
                <Field k="Type" v={detail.data.type} />
                <Field k="Messages" v={detail.data.messageCount.toLocaleString()} />
                <Field k="NSFW" v={detail.data.isNsfw ? 'yes' : 'no'} />
                <Field k="Position" v={String(detail.data.position)} />
              </dl>
              {detail.data.topic && <p className="text-sm text-discord-muted">{detail.data.topic}</p>}
            </div>
          )}
        </Modal>
      )}
    </>
  )
}

function MembersTab() {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)

  const list = useQuery({
    queryKey: ['members', page, pageSize],
    queryFn: () => entitiesApi.members({ page, pageSize }),
    placeholderData: keepPreviousData,
  })

  if (list.isLoading) return <Loading />
  if (list.isError) return <ErrorState error={list.error} />

  return (
    <>
      <TableShell
        head={
          <>
            <Th>User</Th>
            <Th>Nickname</Th>
            <Th>Joined</Th>
            <Th>Status</Th>
          </>
        }
      >
        {list.data!.items.map((m) => (
          <tr key={m.id} className="border-b border-discord-border/40">
            <td className="px-3 py-1.5">
              <UserChip name={m.username} id={m.userDiscordId} />
            </td>
            <td className="px-3 py-1.5 text-discord-muted">{m.nickname ?? '—'}</td>
            <td className="px-3 py-1.5 text-discord-faint" title={m.joinedAtUtc ? formatTimestamp(m.joinedAtUtc).title : ''}>
              {m.joinedAtUtc ? formatTimestamp(m.joinedAtUtc).text : '—'}
            </td>
            <td className="px-3 py-1.5 text-discord-faint">
              {m.timeoutUntilUtc ? 'timed out' : m.isPending ? 'pending' : 'active'}
            </td>
          </tr>
        ))}
      </TableShell>
      <div className="mt-4">
        <Pagination page={page} pageSize={pageSize} total={list.data!.totalCount} onPageChange={setPage} onPageSizeChange={(s) => (setPageSize(s), setPage(1))} />
      </div>
    </>
  )
}

function MessagesTab() {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [selected, setSelected] = useState<string | null>(null)

  const list = useQuery({
    queryKey: ['messages', page, pageSize],
    queryFn: () => entitiesApi.messages({ page, pageSize }),
    placeholderData: keepPreviousData,
  })
  const detail = useQuery({
    queryKey: ['message', selected],
    queryFn: () => entitiesApi.message(selected!),
    enabled: selected !== null,
  })

  if (list.isLoading) return <Loading />
  if (list.isError) return <ErrorState error={list.error} />

  return (
    <>
      <TableShell
        head={
          <>
            <Th>When</Th>
            <Th>Author</Th>
            <Th>Channel</Th>
            <Th>Content</Th>
          </>
        }
      >
        {list.data!.items.map((m) => (
          <tr
            key={m.id}
            onClick={() => setSelected(m.id)}
            className="cursor-pointer border-b border-discord-border/40 hover:bg-discord-bg-card"
          >
            <td className="whitespace-nowrap px-3 py-1.5 text-discord-faint" title={formatTimestamp(m.createdAtUtc).title}>
              {relativeTime(m.createdAtUtc)}
            </td>
            <td className="px-3 py-1.5">
              <UserChip name={m.authorName} />
            </td>
            <td className="px-3 py-1.5">
              <ChannelChip name={m.channelName} />
            </td>
            <td className="px-3 py-1.5 text-discord-text">
              {m.isDeleted && <span className="mr-1 text-discord-red">[deleted]</span>}
              {truncate(m.content ?? '', 80)}
              {m.editedAtUtc && <span className="ml-1 text-xs text-discord-faint">(edited)</span>}
            </td>
          </tr>
        ))}
      </TableShell>
      <div className="mt-4">
        <Pagination page={page} pageSize={pageSize} total={list.data!.totalCount} onPageChange={setPage} onPageSizeChange={(s) => (setPageSize(s), setPage(1))} />
      </div>

      {selected && (
        <Modal title="Message" onClose={() => setSelected(null)}>
          {detail.isLoading && <Loading />}
          {detail.data && (
            <div className="space-y-3">
              <div className="flex flex-wrap items-center gap-3 text-sm">
                <UserChip name={detail.data.authorName} id={detail.data.authorDiscordId} />
                <ChannelChip name={detail.data.channelName} id={detail.data.channelDiscordId} />
                <span className="text-discord-faint">{formatTimestamp(detail.data.createdAtUtc).text}</span>
              </div>
              <p className="whitespace-pre-wrap rounded bg-discord-bg-dark p-3 text-sm text-discord-text">
                {detail.data.content || '(no text content)'}
              </p>
              {detail.data.editHistory.length > 0 && (
                <div>
                  <h4 className="mb-2 text-sm font-semibold text-white">Edit history</h4>
                  <ul className="space-y-2 text-sm">
                    {detail.data.editHistory.map((e, i) => (
                      <li key={i} className="rounded bg-discord-bg-dark p-2 text-discord-muted">
                        <span className="text-discord-faint">{formatTimestamp(e.editedAtUtc).text}</span>
                        <div className="mt-1">
                          <span className="text-discord-red line-through">{e.contentBefore ?? '∅'}</span>
                          {' → '}
                          <span className="text-discord-green">{e.contentAfter ?? '∅'}</span>
                        </div>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          )}
        </Modal>
      )}
    </>
  )
}

function Field({ k, v }: { k: string; v: string }) {
  return (
    <div>
      <dt className="text-xs text-discord-faint">{k}</dt>
      <dd className="text-discord-text">{v}</dd>
    </div>
  )
}
