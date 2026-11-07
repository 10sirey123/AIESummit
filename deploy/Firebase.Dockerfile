FROM node:22-alpine
WORKDIR /app
RUN npm install --global firebase-tools@14.12.1 --no-audit --no-fund
COPY firebase.json .
CMD ["firebase","emulators:start","--only","auth","--project","demo-aiesummit"]
