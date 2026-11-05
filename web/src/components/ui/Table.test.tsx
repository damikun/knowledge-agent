import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '../../test/a11y';
import { Table, TableCell, TableHead, TableHeader, TableRow, TableRowHeader } from './Table';

function Widgets({ selected }: { selected?: string }) {
  return (
    <Table aria-label="Widgets">
      <TableHead>
        <TableHeader>Name</TableHeader>
        <TableHeader>Public key</TableHeader>
        <TableHeader numeric>Chats</TableHeader>
      </TableHead>
      <tbody>
        {[
          { name: 'Demo shop', key: 'wk_demo', chats: 1204 },
          { name: 'Help center', key: 'wk_help', chats: 87 },
        ].map((widget) => (
          <TableRow key={widget.key} selected={widget.name === selected}>
            <TableRowHeader>{widget.name}</TableRowHeader>
            <TableCell mono>{widget.key}</TableCell>
            <TableCell numeric>{widget.chats}</TableCell>
          </TableRow>
        ))}
      </tbody>
    </Table>
  );
}

describe('Table', () => {
  it('names each row by its header cell and the columns by the header row', async () => {
    render(<Widgets />);

    const table = screen.getByRole('table', { name: 'Widgets' });
    expect(within(table).getAllByRole('columnheader').map((cell) => cell.textContent)).toEqual(['Name', 'Public key', 'Chats']);
    expect(within(table).getAllByRole('rowheader').map((cell) => cell.textContent)).toEqual(['Demo shop', 'Help center']);
    expect(await axeViolations()).toEqual([]);
  });

  it('right-aligns numbers in tabular figures and sets ids in mono', () => {
    render(<Widgets />);

    expect(screen.getByRole('columnheader', { name: 'Chats' })).toHaveClass('text-right');
    expect(screen.getByRole('cell', { name: '1204' })).toHaveClass('text-right', 'tabular-nums');
    expect(screen.getByRole('cell', { name: 'wk_demo' })).toHaveClass('font-mono');
  });

  it('marks the selected row', () => {
    render(<Widgets selected="Help center" />);

    const [, demo, help] = screen.getAllByRole('row');
    expect(help).toHaveAttribute('data-selected', 'true');
    expect(demo).not.toHaveAttribute('data-selected');
  });
});
