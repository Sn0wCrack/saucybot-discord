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
        return this.client.connect();
    }

    public has(key: string) {
        return this.client.exists(key);
    }

    public get(key: string) {
        return this.client.get(key);
    }

    public set(key: string, value: string, expireIn = 86400) {
        return this.client.setEx(key, expireIn, value);
    }
}

export default CacheManager;
