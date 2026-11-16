import { Navigate, useLocation, useParams, type RouteObject } from 'react-router';
import { isAdmin, useMe } from '../features/auth/api';
import { RequireAdmin } from '../features/auth/RequireAdmin';
import { ChatPage } from '../features/chat/ChatPage';
import { AppLayout } from './AppLayout';
import { AuthGate } from './AuthGate';
import { AppLoading, NotFoundPage, RouteErrorPage } from './RouteErrorPage';

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

export const routes: RouteObject[] = [
  {
    path: '/',
    element: (
      <AuthGate>
        <AppLayout />
      </AuthGate>
    ),
    errorElement: <RouteErrorPage />,
    hydrateFallbackElement: <AppLoading />,
    children: [
      { index: true, element: <Landing /> },
      { path: 'chat/:conversationId?', element: <ChatRedirect /> },
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
        element: <RequireAdmin />,
        children: [
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
          { path: 'integrations', element: <Navigate to="/notifications" replace /> },
          { path: 'connections', element: <Navigate to="/notifications" replace /> },
        ],
      },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
];
