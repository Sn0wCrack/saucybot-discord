class Environment
{
    static get(key: any, fallback: any = null): any {
        return process.env[key] ?? fallback;
    }
}

export default Environment;
