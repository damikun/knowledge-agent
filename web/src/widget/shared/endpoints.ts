import { endpoint } from '../../lib/endpoint';

export const widgetEndpoints = {
  config: endpoint('GET', '/api/public/widgets/{key}/config'),
  startSession: endpoint('POST', '/api/public/widgets/{key}/sessions'),
  updateAttributes: endpoint('POST', '/api/public/widgets/{key}/attributes'),
  saveContact: endpoint('POST', '/api/public/widgets/{key}/contact'),
  sendMessage: endpoint('POST', '/api/public/widgets/{key}/messages'),
  listConversations: endpoint('GET', '/api/public/widgets/{key}/conversations'),
  getConversation: endpoint('GET', '/api/public/widgets/{key}/conversations/{id}'),
  followConversation: endpoint('GET', '/api/public/widgets/{key}/conversations/{id}/events'),
  requestHandover: endpoint('POST', '/api/public/widgets/{key}/conversations/{id}/handover'),
  cancelHandover: endpoint('POST', '/api/public/widgets/{key}/conversations/{id}/handover/cancel'),
  closeConversation: endpoint('POST', '/api/public/widgets/{key}/conversations/{id}/close'),
  sendFeedback: endpoint('POST', '/api/public/widgets/{key}/conversations/{id}/feedback'),
  previewSession: endpoint('POST', '/api/widgets/{id}/preview-session'),
  frame: endpoint('GET', '/widget/v1/frame/{key}'),
} as const;
