import { Navigate, useLocation, useParams, type RouteObject } from 'react-router';
import { isAdmin, useMe } from '../features/auth/api';
import { RequireAdmin } from '../features/auth/RequireAdmin';
import { ChatPage } from '../features/chat/ChatPage';
import { AppLayout } from './AppLayout';
import { AuthGate } from './AuthGate';
import { AppLoading, NotFoundPage, RouteErrorPage } from './RouteErrorPage';

// Admins land on Home, support users in the Inbox.
// AuthGate renders the routes only once the user is known.
function Landing() {
  return <Navigate to={isAdmin(useMe().data) ? '/home' : '/inbox'} replace />;
}

function ChatRedirect() {
  const { conversationId } = useParams();
  const { search, state } = useLocation();
  return <Navigate to={`/playground${conversationId ? `/${conversationId}` : ''}${search}`} state={state} replace />;
}

function PeopleRedirect() {
  const { personId } = useParams();
  return <Navigate to={`/contacts${personId ? `/${personId}` : ''}`} replace />;
}

function JobsRedirect() {
  const { search, hash, state } = useLocation();
  return <Navigate to={`/system${search}${hash}`} state={state} replace />;
}

// The playground loads eagerly; the other pages are split into their own chunks.
export const routes: RouteObject[] = [
  {
    path: '/',
    element: (
      <AuthGate>
        <AppLayout />
      </AuthGate>
    ),
    errorElement: <RouteErrorPage />,
    // Shown while the chunk of a lazy route opened directly by URL loads.
    hydrateFallbackElement: <AppLoading />,
    children: [
      { index: true, element: <Landing /> },
      { path: 'chat/:conversationId?', element: <ChatRedirect /> },
      // Handover emails link to /inbox/{conversationId}, which redirects to ?c=.
      {
        path: 'inbox/:conversationId?',
        lazy: async () => ({ Component: (await import('../features/inbox/InboxPage')).InboxPage }),
      },
      {
        path: 'conversations',
        lazy: async () => ({ Component: (await import('../features/inbox/ConversationsRedirect')).ConversationsRedirect }),
      },
      {
        path: 'contacts',
        lazy: async () => ({ Component: (await import('../features/people/PeoplePage')).PeoplePage }),
      },
      { path: 'people/:personId?', element: <PeopleRedirect /> },
      {
        path: 'contacts/:personId',
        lazy: async () => ({ Component: (await import('../features/people/PersonPage')).PersonPage }),
      },
      {
        path: 'feedback',
        lazy: async () => ({ Component: (await import('../features/feedback/FeedbackPage')).FeedbackPage }),
      },
      {
        // Support users get a panel that says the page is for admins.
        element: <RequireAdmin />,
        children: [
          // One route for both, so the playground keeps its streaming state when a new conversation gets its id.
          { path: 'playground/:conversationId?', element: <ChatPage /> },
          {
            path: 'home',
            lazy: async () => ({ Component: (await import('../features/home/HomePage')).HomePage }),
          },
          {
            path: 'analytics',
            lazy: async () => ({ Component: (await import('../features/analytics/AnalyticsPage')).AnalyticsPage }),
          },
          {
            path: 'sources',
            lazy: async () => ({ Component: (await import('../features/sources/SourcesPage')).SourcesPage }),
          },
          {
            path: 'agents',
            lazy: async () => ({ Component: (await import('../features/agents/AgentsPage')).AgentsPage }),
          },
          {
            // ?template=, ?from={id} and ?step= survive a reload and going back.
            path: 'agents/new',
            lazy: async () => ({ Component: (await import('../features/agents/NewAgentWizard')).NewAgentPage }),
          },
          {
            path: 'agents/:agentId',
            lazy: async () => ({ Component: (await import('../features/agents/AgentPage')).AgentPage }),
          },
          {
            path: 'tools',
            lazy: async () => ({ Component: (await import('../features/tools/ToolsPage')).ToolsPage }),
          },
          {
            // "new" opens the wizard.
            path: 'widgets/:widgetId?',
            lazy: async () => ({ Component: (await import('../features/widgets/WidgetsPage')).WidgetsPage }),
          },
          {
            path: 'models',
            lazy: async () => ({ Component: (await import('../features/models/ModelsPage')).ModelsPage }),
          },
          {
            path: 'prompts/:name?',
            lazy: async () => ({ Component: (await import('../features/prompts/PromptsPage')).PromptsPage }),
          },
          {
            path: 'evals',
            lazy: async () => ({ Component: (await import('../features/evals/EvalsPage')).EvalsPage }),
          },
          {
            path: 'evals/runs/:id',
            lazy: async () => ({ Component: (await import('../features/evals/EvalRunPage')).EvalRunPage }),
          },
          {
            path: 'evals/datasets/:id',
            lazy: async () => ({ Component: (await import('../features/evals/DatasetPage')).DatasetPage }),
          },
          {
            path: 'system',
            lazy: async () => ({ Component: (await import('../features/system/SystemPage')).SystemPage }),
          },
          { path: 'jobs', element: <JobsRedirect /> },
          {
            path: 'notifications',
            lazy: async () => ({ Component: (await import('../features/notifications/NotificationsPage')).NotificationsPage }),
          },
          {
            path: 'team',
            lazy: async () => ({ Component: (await import('../features/team/TeamPage')).TeamPage }),
          },
          // Alert links from the API still use /integrations.
          { path: 'integrations', element: <Navigate to="/notifications" replace /> },
          { path: 'connections', element: <Navigate to="/notifications" replace /> },
        ],
      },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
];
