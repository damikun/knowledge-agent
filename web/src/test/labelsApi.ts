/** Labels as GET /api/labels returns them. */
export const QUESTION_LABEL_ID = '01a1ab53-0000-7000-8000-000000000001';
export const PROBLEM_LABEL_ID = '01a1ab53-0000-7000-8000-000000000002';

export function labelDto(overrides: Record<string, unknown> = {}) {
  return {
    id: QUESTION_LABEL_ID,
    name: 'Question',
    color: 'blue',
    description: 'The person asks how something works.',
    autoApply: true,
    conversationCount: 4,
    createdAt: '2026-11-01T09:00:00Z',
    updatedAt: '2026-11-01T09:00:00Z',
    ...overrides,
  };
}

export const labelList = () => [
  labelDto(),
  labelDto({ id: PROBLEM_LABEL_ID, name: 'Problem', color: 'red', description: 'Something is broken.', conversationCount: 2 }),
];
