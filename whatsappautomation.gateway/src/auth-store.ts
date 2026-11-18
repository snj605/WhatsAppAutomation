import type { Pool, PoolClient } from 'pg';
import {
  BufferJSON,
  initAuthCreds,
  proto,
  type AuthenticationCreds,
  type AuthenticationState,
  type SignalDataSet,
  type SignalDataTypeMap,
} from 'baileys';
import { decrypt, encrypt } from './crypto.ts';

// The saved WhatsApp login of one account: the credentials plus the Signal encryption keys.
// Anyone holding it can send as that number without scanning a QR, so every row is encrypted
// with AES-256-GCM. The table is created by the API's database migration
// (whatsapp_auth_state); this file only reads and writes it.

const CREDS = 'creds';
const META = 'meta';
const LINKED = 'linked';

export interface AccountAuthState {
  state: AuthenticationState;
  saveCreds: () => Promise<void>;
}

export class AuthStore {
  readonly #pool: Pool;
  readonly #key: Buffer;

  constructor(pool: Pool, key: Buffer) {
    this.#pool = pool;
    this.#key = key;
  }

  /** Fails early with a clear message when the migration has not been run. */
  async ensureTableExists(): Promise<void> {
    try {
      await this.#pool.query('SELECT 1 FROM whatsapp_auth_state LIMIT 0');
    } catch (error) {
      throw new Error('Table whatsapp_auth_state is missing. Run "dotnet run -- migrate" in the Api project first.', {
        cause: error,
      });
    }
  }

  /** Accounts that finished linking and should be reconnected when the gateway starts. */
  async listLinkedAccounts(): Promise<string[]> {
    const result = await this.#pool.query<{ account_id: string }>(
      'SELECT account_id FROM whatsapp_auth_state WHERE kind = $1 AND key_id = $2',
      [META, LINKED],
    );
    return result.rows.map((r) => r.account_id);
  }

  /** Whether this account has a finished link saved (so it can reconnect without a new QR). */
  async isLinked(accountId: string): Promise<boolean> {
    const result = await this.#pool.query(
      'SELECT 1 FROM whatsapp_auth_state WHERE account_id = $1 AND kind = $2 AND key_id = $3',
      [accountId, META, LINKED],
    );
    return (result.rowCount ?? 0) > 0;
  }

  async markLinked(accountId: string): Promise<void> {
    await this.#write(this.#pool, accountId, META, LINKED, true);
  }

  /** Deletes the whole saved login of an account (unlink, logged out, or a link that did not finish). */
  async clear(accountId: string): Promise<void> {
    await this.#pool.query('DELETE FROM whatsapp_auth_state WHERE account_id = $1', [accountId]);
  }

  /** The Baileys auth state backed by the database, like Baileys' file-based one but encrypted. */
  async load(accountId: string): Promise<AccountAuthState> {
    const creds: AuthenticationCreds = (await this.#read<AuthenticationCreds>(accountId, CREDS, '')) ?? initAuthCreds();

    const state: AuthenticationState = {
      creds,
      keys: {
        get: async <T extends keyof SignalDataTypeMap>(type: T, ids: string[]) => {
          const found: { [id: string]: SignalDataTypeMap[T] } = {};
          if (ids.length === 0) {
            return found;
          }

          const result = await this.#pool.query<{ key_id: string; data: Buffer }>(
            'SELECT key_id, data FROM whatsapp_auth_state WHERE account_id = $1 AND kind = $2 AND key_id = ANY($3)',
            [accountId, type, ids],
          );
          for (const row of result.rows) {
            let value = this.#decode(accountId, type, row.key_id, row.data);
            if (type === 'app-state-sync-key' && value) {
              value = proto.Message.AppStateSyncKeyData.fromObject(value as object);
            }

            found[row.key_id] = value as SignalDataTypeMap[T];
          }

          return found;
        },
        set: async (data: SignalDataSet) => {
          const client = await this.#pool.connect();
          try {
            await client.query('BEGIN');
            for (const type of Object.keys(data) as (keyof SignalDataTypeMap)[]) {
              const entries = data[type] ?? {};
              for (const [id, value] of Object.entries(entries)) {
                if (value) {
                  await this.#write(client, accountId, type, id, value);
                } else {
                  await client.query(
                    'DELETE FROM whatsapp_auth_state WHERE account_id = $1 AND kind = $2 AND key_id = $3',
                    [accountId, type, id],
                  );
                }
              }
            }

            await client.query('COMMIT');
          } catch (error) {
            await client.query('ROLLBACK');
            throw error;
          } finally {
            client.release();
          }
        },
      },
    };

    return { state, saveCreds: () => this.#write(this.#pool, accountId, CREDS, '', creds) };
  }

  async #read<T>(accountId: string, kind: string, keyId: string): Promise<T | null> {
    const result = await this.#pool.query<{ data: Buffer }>(
      'SELECT data FROM whatsapp_auth_state WHERE account_id = $1 AND kind = $2 AND key_id = $3',
      [accountId, kind, keyId],
    );
    const row = result.rows[0];
    return row ? (this.#decode(accountId, kind, keyId, row.data) as T) : null;
  }

  async #write(db: Pool | PoolClient, accountId: string, kind: string, keyId: string, value: unknown): Promise<void> {
    const plaintext = Buffer.from(JSON.stringify(value, BufferJSON.replacer), 'utf8');
    const data = encrypt(this.#key, plaintext, context(accountId, kind, keyId));
    await db.query(
      `INSERT INTO whatsapp_auth_state (account_id, kind, key_id, data, updated_at)
       VALUES ($1, $2, $3, $4, now())
       ON CONFLICT (account_id, kind, key_id) DO UPDATE SET data = EXCLUDED.data, updated_at = now()`,
      [accountId, kind, keyId, data],
    );
  }

  #decode(accountId: string, kind: string, keyId: string, data: Buffer): unknown {
    const plaintext = decrypt(this.#key, data, context(accountId, kind, keyId));
    return JSON.parse(plaintext.toString('utf8'), BufferJSON.reviver);
  }
}

function context(accountId: string, kind: string, keyId: string): string {
  return `${accountId}|${kind}|${keyId}`;
}
