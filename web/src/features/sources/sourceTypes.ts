import { Earth, FileText, FileType, Globe, NotebookText, type LucideIcon } from 'lucide-react';

export const SOURCE_TYPES: Record<string, { icon: LucideIcon; label: string }> = {
  url: { icon: Globe, label: 'Web page' },
  pdf: { icon: FileType, label: 'PDF' },
  text: { icon: FileText, label: 'Text' },
  markdown: { icon: NotebookText, label: 'Markdown' },
  website: { icon: Earth, label: 'Website' },
};
