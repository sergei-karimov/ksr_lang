import * as fs from 'fs';
import * as path from 'path';

export interface ExecutableResolverOptions {
    platform?: NodeJS.Platform;
    pathValue?: string;
    fileExists?: (candidate: string) => boolean;
}

/** Resolve Kestrel first, with Windows installer aliases as legacy fallbacks. */
export function resolveExecutable(
    configured: string,
    options: ExecutableResolverOptions = {},
): string | null {
    const platform = options.platform ?? process.platform;
    const pathApi = platform === 'win32' ? path.win32 : path.posix;
    const fileExists = options.fileExists ?? fs.existsSync;

    if (isExplicitPath(configured, pathApi))
        return fileExists(configured) ? configured : null;

    const canonical = findOnPath('kestrel', platform, options.pathValue, fileExists, pathApi);
    if (canonical) return canonical;

    const legacy = findOnPath('ksr', platform, options.pathValue, fileExists, pathApi);
    if (legacy) return legacy;

    return configured;
}

function isExplicitPath(value: string, pathApi: typeof path.posix): boolean {
    return pathApi.isAbsolute(value) || value.includes('/') || value.includes('\\');
}

function findOnPath(
    command: string,
    platform: NodeJS.Platform,
    pathValue: string | undefined,
    fileExists: (candidate: string) => boolean,
    pathApi: typeof path.posix,
): string | null {
    const names = platform === 'win32'
        ? [`${command}.exe`, `${command}.cmd`, `${command}.ps1`, command]
        : [command];
    const delimiter = platform === 'win32' ? ';' : ':';
    for (const directory of (pathValue ?? process.env['PATH'] ?? '').split(delimiter).filter(Boolean)) {
        for (const name of names) {
            const candidate = pathApi.join(directory, name);
            if (fileExists(candidate)) return candidate;
        }
    }
    return null;
}
