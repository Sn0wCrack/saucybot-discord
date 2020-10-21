# Build Step -- Build out our TypeScript code
FROM node:alpine AS build

WORKDIR /usr/src/app

COPY package*.json ./
COPY tsconfig*.json ./
COPY ./src ./src

# Install depdency packages
# git - pulling git repos
RUN apk add --no-cache --update \
    git

RUN npm ci --quiet && npm run build

# Production Step -- Run our compiled TypeScript code
FROM node:alpine AS production

WORKDIR /bot
ENV NODE_ENV=production

# Install depdency packages
# libc6-compat - makes sure all node functions work correctly
# ffmpeg - used for pixiv ugoira
# git - pulling git repos
RUN apk add --no-cache --update \
    libc6-compat \
    ffmpeg \
    git

COPY package*.json ./
RUN npm ci --quiet --only=production

COPY --from=build /usr/src/app/dist ./dist

CMD ["node", "/bot/dist/src/index.js"]
