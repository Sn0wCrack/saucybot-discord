/* eslint-disable @typescript-eslint/no-explicit-any */
/* eslint-disable @typescript-eslint/explicit-module-boundary-types */
import chalk from 'chalk';
import { DateTime } from 'luxon';

class Logger {
    static fatal(message: any, source: string | number = ''): void {
        console.error(this.format(message, LogLevel.FATAL, source));
    }

    static error(message: any, source: string | number = ''): void {
        console.error(this.format(message, LogLevel.ERROR, source));
    }

    static warn(message: any, source: string | number = ''): void {
        console.warn(this.format(message, LogLevel.WARN, source));
    }

    static info(message: any, source: string | number = ''): void {
        console.info(this.format(message, LogLevel.INFO, source));
    }

    static debug(message: any, source: string | number = ''): void {
        console.debug(this.format(message, LogLevel.DEBUG, source));
    }

    static trace(message: any, source: string | number = ''): void {
        console.debug(this.format(message, LogLevel.TRACE, source));
    }

    private static format(
        message: any,
        level: LogLevel,
        source: string | number = ''
    ): string {
        const now = DateTime.local();
        const time = now.toFormat('yyyy-MM-dd HH:mm:ss');

        const color = {
            fatal: chalk.red,
            error: chalk.red,
            warn: chalk.yellow,
            info: chalk.blue,
            debug: chalk.blue,
            trace: chalk.green,
        }[level];

        let formatted = `[${time}]`;

        if (source) {
            formatted += ` [${source}]`;
        }

        formatted += ` [${level.toUpperCase()}] ${message}`;

        return color(formatted);
    }
}

enum LogLevel {
    FATAL = 'fatal',
    ERROR = 'error',
    WARN = 'warn',
    INFO = 'info',
    DEBUG = 'debug',
    TRACE = 'trace',
}

export default Logger;
