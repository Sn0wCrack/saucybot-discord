/* eslint-disable @typescript-eslint/no-explicit-any */
/* eslint-disable @typescript-eslint/explicit-module-boundary-types */
import chalk from 'chalk';
import { DateTime } from 'luxon';

class Logger {
    static fatal(message: any): void {
        console.error(this.format(message, LogLevel.FATAL));
    }

    static error(message: any): void {
        console.error(this.format(message, LogLevel.ERROR));
    }

    static warn(message: any): void {
        console.warn(this.format(message, LogLevel.WARN));
    }

    static info(message: any): void {
        console.info(this.format(message, LogLevel.INFO));
    }

    static debug(message: any): void {
        console.debug(this.format(message, LogLevel.DEBUG));
    }

    static trace(message: any): void {
        console.debug(this.format(message, LogLevel.TRACE));
    }

    private static format(message: any, level: LogLevel): string {
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

        return color(`[${time}] [${level.toUpperCase()}] ${message}`);
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
