import { htmlToText } from 'html-to-text';

export function delay(ms: number): Promise<void> {
    return new Promise<void>((resolve) => setTimeout(resolve, ms));
}

export function processDescription(description: string): string {
    description = htmlToText(description);

    if (description.length > 300) {
        description = description.substring(0, 300) + '...';
    }

    return description;
}
