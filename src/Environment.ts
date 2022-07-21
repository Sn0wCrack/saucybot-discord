class Environment {
    static get(key: string | number, fallback: unknown = null): unknown {
        return process.env[key] ?? fallback;
    }

    static isDevelopment(): boolean {
        const environment = Environment.get('NODE_ENV') as string;

        return ['dev', 'development'].includes(environment.toLowerCase());
    }

    static isProduction(): boolean {
        const environment = Environment.get('NODE_ENV') as string;

        return ['prod', 'production'].includes(environment.toLowerCase());
    }
}

export default Environment;
