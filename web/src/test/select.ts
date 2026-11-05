import { screen } from '@testing-library/react';
import type { UserEvent } from '@testing-library/user-event';

/** Picks an option of components/ui/Select the way a person does: open the listbox, click the option. */
export async function chooseOption(user: UserEvent, combobox: HTMLElement, option: string) {
  await user.click(combobox);
  await user.click(await screen.findByRole('option', { name: option }));
}
