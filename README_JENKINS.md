# Deploying Jenkins in WSL

This directory contains a `docker-compose.yml` file to quickly deploy Jenkins using Docker.

## Prerequisites

- Docker Desktop installed with WSL 2 backend integration enabled.
- WSL distribution running.

## Quick Start

1. **Start Jenkins:**
   open a terminal in this directory and run:
   ```bash
   docker compose up -d
   ```

2. **Access Jenkins:**
   Open your browser and navigate to: [http://localhost:8080](http://localhost:8080)

3. **Get Initial Admin Password:**
   Run the following command to see the logs and find the password:
   ```bash
   docker compose logs jenkins
   ```
   Look for a block of text like:
   ```text
   *************************************************************
   *************************************************************
   *************************************************************

   Jenkins initial setup is required. An admin user has been created and a password generated.
   Please use the following password to proceed to installation:

   <YOUR_PASSWORD_HERE>

   This may also be found at: /var/jenkins_home/secrets/initialAdminPassword
   ```

## Notes for WSL Users

- **Permissions**: The `docker-compose.yml` is configured to run Jenkins as `root` (`user: root`). This is often necessary in WSL when mounting directories from the Windows filesystem (like `./jenkins_home`) to avoid "Permission denied" errors.
- **Persistence**: All Jenkins data is stored in the `./jenkins_home` directory created in your current folder.
