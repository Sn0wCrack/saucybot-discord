import * as Redis from 'redis';
import Environment from './Environment';

class CacheManager {
    private static instance: CacheManager;

    private client;

    private constructor() {
        this.client = Redis.createClient({
            url: Environment.get('REDIS_URL') as string,
            username: Environment.get('REDIS_USERNAME') as string,
            password: Environment.get('REDIS_PASSWORD') as string,
            name: Environment.get('REDIS_NAME') as string,
            database: Environment.get('REDIS_DATABASE', 0) as number,
        });
    }

    public static async getInstance(): Promise<CacheManager> {
        if (!CacheManager.instance) {
            CacheManager.instance = new CacheManager();
            await CacheManager.instance.connect();
        }

        return Promise.resolve(CacheManager.instance);
    }

    public async connect() {
        if (!this.client) {
            return Promise.resolve();
        }

        return this.client.connect();
    }

    public async has(key: string): Promise<boolean> {
        if (!this.client) {
            return Promise.resolve(false);
        }

        const result = await this.client.exists(key);

        return Promise.resolve(result === 1);
    }

    public get(key: string) {
        return this.client.get(key);
    }

    public set(key: string, value: string, expireIn = 86400) {
        if (!this.client) {
            Promise.resolve();
        }

        return this.client.setEx(key, expireIn, value);
    }

    public async remember(
        key: string,
        value: string | (() => Promise<string>),
        expireIn = 86400
    ): Promise<string> {
        const exists = await this.has(key);

        if (exists) {
            return this.get(key);
        }

        if (typeof value === 'function') {
            value = await value();
        }

        await this.set(key, value, expireIn);

        return Promise.resolve(value);
    }
}

export default CacheManager;
