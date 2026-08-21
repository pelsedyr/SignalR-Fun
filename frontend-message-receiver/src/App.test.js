import { render, screen } from '@testing-library/react';
import App from './App';

test('renders the SignalR receiver interface', () => {
  render(<App />);
  expect(screen.getByRole('heading', { name: /følg varslene/i })).toBeInTheDocument();
  expect(screen.getByLabelText(/mottaker-id/i)).toHaveValue('user-123');
});
