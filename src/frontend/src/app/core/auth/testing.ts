import { Me } from './auth.models';

export function me(overrides: Partial<Me> = {}): Me {
  return {
    id: '01a1-user',
    email: 'anna@example.test',
    displayName: 'Anna Nowak',
    role: 'user',
    ...overrides,
  };
}
