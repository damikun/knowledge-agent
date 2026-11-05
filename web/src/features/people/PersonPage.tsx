import { BadgeCheck, Download, Trash2, UserRound } from 'lucide-react';
import { useState } from 'react';
import { useParams } from 'react-router';
import { Avatar } from '../../components/ui/Avatar';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { EmptyState, ErrorMessage, PageHeader } from '../../components/ui/feedback';
import { Skeleton } from '../../components/ui/Skeleton';
import { StatCard } from '../../components/ui/StatCard';
import { toast } from '../../components/ui/Toast';
import { ApiError, errorMessage } from '../../lib/api';
import { useBreadcrumb } from '../../lib/breadcrumb';
import { formatDateTime, formatNumber, formatRelativeTime } from '../../lib/format';
import { isAdmin, useMe } from '../auth/api';
import { useExportPerson, usePerson } from './api';
import { ErasePersonDialog } from './ErasePersonDialog';
import { PersonConversations } from './PersonConversations';
import { AboutCard, DetailsCard } from './PersonDetails';

export function PersonPage() {
  const { personId = '' } = useParams();
  const person = usePerson(personId);
  useBreadcrumb(person.data ? person.data.name : null);
  const admin = isAdmin(useMe().data);
  const [erasing, setErasing] = useState(false);
  const exportPerson = useExportPerson(personId);

  if (person.isPending) {
    return (
      <div className="mx-auto max-w-6xl space-y-4 px-4 py-6 lg:px-6" aria-busy="true" aria-label="Loading person">
        <Skeleton className="h-8 w-60" />
        <Skeleton shape="card" className="h-40" />
      </div>
    );
  }

  if (person.isError) {
    const missing = person.error instanceof ApiError && person.error.status === 404;
    return (
      <div className="mx-auto max-w-6xl space-y-4 px-4 py-6 lg:px-6">
        {missing ? (
          <EmptyState icon={UserRound} title="This person does not exist" description="They may have been erased, or retention removed them." />
        ) : (
          <ErrorMessage error={person.error} title="The person could not be loaded" />
        )}
      </div>
    );
  }

  const data = person.data;
  return (
    <div className="relative h-full overflow-y-auto">
      <div className="mx-auto max-w-6xl space-y-6 px-4 py-6 lg:px-6">
        <div className="flex items-start gap-4">
          <Avatar name={data.name} size="lg" {...(data.identified || data.email ? {} : { icon: UserRound })} />
          <div className="min-w-0 flex-1">
            <PageHeader
              title={data.name}
              description={
                <span className="flex flex-wrap items-center gap-2">
                  {data.identified ? (
                    <Badge tone="accent">
                      <BadgeCheck className="size-3" aria-hidden />
                      Signed in
                    </Badge>
                  ) : (
                    <Badge>Anonymous</Badge>
                  )}
                  <span>{data.email ?? 'No email'}</span>
                </span>
              }
              actions={
                admin ? (
                  <>
                    <Button
                      icon={Download}
                      loading={exportPerson.isPending}
                      onClick={() =>
                        exportPerson.mutate(undefined, { onError: (error) => toast({ title: 'The export failed', description: errorMessage(error), tone: 'danger' }) })
                      }
                    >
                      Export
                    </Button>
                    <Button variant="danger-ghost" icon={Trash2} onClick={() => setErasing(true)}>
                      Erase
                    </Button>
                  </>
                ) : undefined
              }
            />
          </div>
        </div>

        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard label="Conversations" value={formatNumber(data.stats.conversations)} />
          <StatCard label="Requests for a person" value={formatNumber(data.stats.handovers)} />
          <StatCard
            label="Average rating"
            value={data.stats.averageRating === null ? '–' : `${data.stats.averageRating.toFixed(1)}/5`}
            caption={data.stats.ratings === 0 ? 'No chat ratings' : `From ${formatNumber(data.stats.ratings)} ${data.stats.ratings === 1 ? 'rating' : 'ratings'}`}
          />
          <StatCard label="Last seen" value={formatRelativeTime(data.stats.lastSeenAt)} caption={formatDateTime(data.stats.lastSeenAt)} />
        </div>

        <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_360px]">
          <PersonConversations person={data} />
          <div className="space-y-6">
            <DetailsCard person={data} admin={admin} />
            <AboutCard person={data} admin={admin} />
          </div>
        </div>
      </div>
      {admin ? <ErasePersonDialog person={data} open={erasing} onClose={() => setErasing(false)} /> : null}
    </div>
  );
}
