/*
 * macOS Native Launcher for RedHoleGame
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <libgen.h>
#include <mach-o/dyld.h>

#define MAX_PATH 4096

static int file_exists(const char *path) {
    return access(path, F_OK) == 0;
}

int main(int argc, char *argv[]) {
    char exe_path[MAX_PATH];
    uint32_t size = MAX_PATH;

    if (_NSGetExecutablePath(exe_path, &size) != 0) {
        fprintf(stderr, "Error: Could not get executable path\n");
        return 1;
    }

    char *real_path = realpath(exe_path, NULL);
    if (!real_path) {
        fprintf(stderr, "Error: Could not resolve path\n");
        return 1;
    }

    char *dir = dirname(real_path);

    char dll_path[MAX_PATH];
    char icd_path[MAX_PATH];
    char dyld_path[MAX_PATH];
    char native_path[MAX_PATH];

    char exe_name[MAX_PATH];
    strncpy(exe_name, basename(real_path), MAX_PATH - 1);
    exe_name[MAX_PATH - 1] = '\0';

    char dll_name[MAX_PATH];
    snprintf(dll_name, MAX_PATH, "%s.dll", exe_name);

    snprintf(dll_path, MAX_PATH, "%s/%s", dir, dll_name);
    snprintf(icd_path, MAX_PATH, "%s/runtimes/osx/native/MoltenVK_icd.json", dir);
    snprintf(dyld_path, MAX_PATH, "%s", dir);
    snprintf(native_path, MAX_PATH, "%s/runtimes/osx/native", dir);

    const char *existing_dyld = getenv("DYLD_LIBRARY_PATH");
    if (existing_dyld && strlen(existing_dyld) > 0) {
        char new_dyld[MAX_PATH * 2];
        snprintf(new_dyld, sizeof(new_dyld), "%s:%s:%s", dyld_path, native_path, existing_dyld);
        setenv("DYLD_LIBRARY_PATH", new_dyld, 1);
    } else {
        char new_dyld[MAX_PATH * 2];
        snprintf(new_dyld, sizeof(new_dyld), "%s:%s", dyld_path, native_path);
        setenv("DYLD_LIBRARY_PATH", new_dyld, 1);
    }

    if (file_exists("/opt/homebrew/lib/libvulkan.dylib")) {
        const char *dyld = getenv("DYLD_LIBRARY_PATH");
        if (dyld && !strstr(dyld, "/opt/homebrew/lib")) {
            char new_dyld[MAX_PATH * 2];
            snprintf(new_dyld, sizeof(new_dyld), "%s:/opt/homebrew/lib", dyld);
            setenv("DYLD_LIBRARY_PATH", new_dyld, 1);
        }
    }

    if (!file_exists(icd_path)) {
        const char *brew_icd = "/opt/homebrew/share/vulkan/icd.d/MoltenVK_icd.json";
        const char *local_icd = "/usr/local/share/vulkan/icd.d/MoltenVK_icd.json";
        if (file_exists(brew_icd)) {
            strncpy(icd_path, brew_icd, MAX_PATH - 1);
            icd_path[MAX_PATH - 1] = '\0';
        } else if (file_exists(local_icd)) {
            strncpy(icd_path, local_icd, MAX_PATH - 1);
            icd_path[MAX_PATH - 1] = '\0';
        }
    }
    setenv("VK_ICD_FILENAMES", icd_path, 1);
    setenv("VK_DRIVER_FILES", icd_path, 1);

    char **new_argv = malloc((argc + 3) * sizeof(char *));
    new_argv[0] = "dotnet";
    new_argv[1] = dll_path;

    for (int i = 1; i < argc; i++) {
        new_argv[i + 1] = argv[i];
    }
    new_argv[argc + 1] = NULL;

    execvp("dotnet", new_argv);

    fprintf(stderr, "Error: Could not execute dotnet\n");
    free(new_argv);
    free(real_path);
    return 1;
}
