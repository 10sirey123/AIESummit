FROM node:24-alpine
WORKDIR /app
COPY server.mjs ./
USER node
EXPOSE 8080
HEALTHCHECK --interval=4s --timeout=3s --retries=15 CMD wget -qO- http://localhost:8080/health || exit 1
CMD ["node", "server.mjs"]
