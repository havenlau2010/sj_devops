const http = require('http');
const fs = require('fs');
const path = require('path');
const { spawn, exec } = require('child_process');

const CONFIG_PATH = path.resolve(__dirname, 'config.json');
let config = {};

// Load Config
try {
    const data = fs.readFileSync(CONFIG_PATH, 'utf8');
    config = JSON.parse(data);
} catch (err) {
    console.error('Error reading config.json:', err);
    process.exit(1);
}

// Helper to Run Command
function runCommand(command, args, cwd) {
    return new Promise((resolve, reject) => {
        const cmdStr = `${command} ${args.join(' ')}`;
        console.log(`[START] ${cmdStr} in ${cwd}`);
        
        const proc = spawn(command, args, { cwd, shell: true });
        let stdout = '';
        let stderr = '';

        proc.stdout.on('data', (data) => {
            const str = data.toString();
            stdout += str;
            console.log(`[STDOUT] ${str.trim()}`);
        });

        proc.stderr.on('data', (data) => {
            const str = data.toString();
            stderr += str;
            console.error(`[STDERR] ${str.trim()}`);
        });

        proc.on('error', (err) => {
             console.error(`[ERROR] Failed to start command: ${cmdStr}`, err);
             reject(err);
        });

        proc.on('close', (code) => {
            console.log(`[END] ${cmdStr} exited with code ${code}`);
            if (code === 0) {
                resolve({ code, stdout, stderr });
            } else {
                reject({ code, stdout, stderr, command: cmdStr });
            }
        });
    });
}

// Copy Directory Recursively
function copyDir(src, dest) {
    if (!fs.existsSync(dest)) {
        fs.mkdirSync(dest, { recursive: true });
    }
    const entries = fs.readdirSync(src, { withFileTypes: true });

    for (const entry of entries) {
        const srcPath = path.join(src, entry.name);
        const destPath = path.join(dest, entry.name);

        if (entry.isDirectory()) {
            copyDir(srcPath, destPath);
        } else {
            fs.copyFileSync(srcPath, destPath);
        }
    }
}

const server = http.createServer(async (req, res) => {
    if (req.method === 'GET' && req.url === '/run') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        const results = {
            steps: [],
            success: true
        };

        const logStep = (name, status, details = '') => {
            results.steps.push({ name, status, details, time: new Date().toISOString() });
        };

        try {
            console.log('Received /run request...');
            
            // 1. SVN Update (Parallel or Sequential? Let's do Parallel for speed)
            logStep('SVN Update', 'Running');
            const svnPromises = config.projects.map(p => {
                const projectPath = path.resolve(__dirname, p.path);
                // Assume svn client is installed and 'svn update' works
                return runCommand('svn', ['update'], projectPath)
                    .then(out => ({ name: p.name, status: 'success', output: out.stdout }))
                    .catch(err => ({ name: p.name, status: 'error', error: err }));
            });

            const svnResults = await Promise.all(svnPromises);
            results.svn = svnResults;
            
            const svnFailures = svnResults.filter(r => r.status === 'error');
            if (svnFailures.length > 0) {
                throw new Error(`SVN Update failed for: ${svnFailures.map(r => r.name).join(', ')}`);
            }
            logStep('SVN Update', 'Success');

            // 2. Build (Parallel)
            logStep('Build', 'Running');
            const buildPromises = config.projects.map(p => {
                const projectPath = path.resolve(__dirname, p.path);
                const [cmd, ...args] = p.buildCmd.split(' ');
                return runCommand(cmd, args, projectPath)
                    .then(out => ({ name: p.name, status: 'success', output: out.stdout }))
                    .catch(err => ({ name: p.name, status: 'error', error: err }));
            });

            const buildResults = await Promise.all(buildPromises);
            results.build = buildResults;

            const buildFailures = buildResults.filter(r => r.status === 'error');
            if (buildFailures.length > 0) {
                throw new Error(`Build failed for: ${buildFailures.map(r => r.name).join(', ')}`);
            }
            logStep('Build', 'Success');

            // 3. Copy Results
            logStep('Copy Artifacts', 'Running');
            // Ensure output dir exists
            if (!fs.existsSync(config.outputDir)) {
                fs.mkdirSync(config.outputDir, { recursive: true });
            }

            config.projects.forEach(p => {
                const projectPath = path.resolve(__dirname, p.path);
                const distPath = path.join(projectPath, p.distDir || 'dist');
                const targetPath = path.join(config.outputDir, p.name);

                if (fs.existsSync(distPath)) {
                    copyDir(distPath, targetPath);
                    console.log(`Copied ${distPath} to ${targetPath}`);
                } else {
                    console.warn(`Dist directory not found for ${p.name}: ${distPath}`);
                    results.steps.push({ name: `Copy ${p.name}`, status: 'warning', details: 'Dist dir not found' });
                }
            });
            logStep('Copy Artifacts', 'Success');

            res.end(JSON.stringify(results, null, 2));

        } catch (error) {
            console.error('Workflow failed:', error);
            results.success = false;
            results.error = error.message;
            res.end(JSON.stringify(results, null, 2));
        }

    } else {
        res.writeHead(404);
        res.end('Not Found');
    }
});

server.listen(config.port, () => {
    console.log(`Build server running on http://localhost:${config.port}`);
    console.log(`Target Output Dir: ${config.outputDir}`);
});
