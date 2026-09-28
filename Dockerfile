# syntax=docker/dockerfile:1.6

# ---------- Builder：编译 better-sqlite3 原生模块 ----------
FROM node:20-alpine AS builder
# better-sqlite3 需 python3/make/g++ 编译；alpine 上没有 prebuilt 二进制
RUN apk add --no-cache python3 make g++
WORKDIR /app
COPY package.json package-lock.json* ./
RUN npm install --omit=dev --no-audit --no-fund

# ---------- Runtime：仅复制产物，镜像最小化 ----------
FROM node:20-alpine AS runtime
# better-sqlite3 运行期仍需 libstdc++；tini 做 PID1 处理僵尸进程与信号转发
RUN apk add --no-cache libstdc++ tini
WORKDIR /app
ENV NODE_ENV=production \
    DATA_DIR=/app/data \
    TZ=Asia/Shanghai
COPY --from=builder /app/node_modules ./node_modules
COPY package.json server.js ./
COPY public ./public
RUN mkdir -p /app/data
EXPOSE 3000
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD node -e "require('http').get('http://127.0.0.1:'+(process.env.PORT||3000)+'/',r=>process.exit(r.statusCode<500?0:1)).on('error',()=>process.exit(1))"
ENTRYPOINT ["/sbin/tini","--"]
CMD ["node","server.js"]
