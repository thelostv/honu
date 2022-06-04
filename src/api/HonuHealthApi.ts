﻿import { Loading } from "Loading";
import ApiWrapper from "api/ApiWrapper";

export class HonuHealth {
    public queues: ServiceQueueCount[] = [];
    public death: CensusRealtimeHealthEntry[] = [];
    public exp: CensusRealtimeHealthEntry[] = [];
    public realtimeHealthFailures: BadHealthEntry[] = [];
}

export class ServiceQueueCount {
    public queueName: string = "";
    public count: number = 0;
}

export class CensusRealtimeHealthEntry {
    public worldID: number = 0;
    public lastEvent: Date = new Date();
    public failureCount: number = 0;
}

export class BadHealthEntry {
    public when: Date = new Date();
    public what: string = "";
}

export class HonuHealthApi extends ApiWrapper<HonuHealth> {
    private static _instance: HonuHealthApi = new HonuHealthApi();
    public static get(): HonuHealthApi { return HonuHealthApi._instance; }

    public static parseQueue(elem: any): ServiceQueueCount {
        return {
            ...elem
        };
    }

    public static parseRealtime(elem: any): CensusRealtimeHealthEntry {
        return {
            ...elem,
            lastEvent: new Date(elem.lastEvent)
        };
    }

    public static parseBadHealth(elem: any): BadHealthEntry {
        return {
            ...elem,
            when: new Date(elem.when)
        };
    }

    public static parse(elem: any): HonuHealth {
        return {
            queues: elem.queues.map((iter: any) => HonuHealthApi.parseQueue(iter)),
            death: elem.death.map((iter: any) => HonuHealthApi.parseRealtime(iter)),
            exp: elem.exp.map((iter: any) => HonuHealthApi.parseRealtime(iter)),
            realtimeHealthFailures: elem.realtimeHealthFailures.map((iter: any) => HonuHealthApi.parseBadHealth(iter))
        };
    }

    public static async getHealth(): Promise<Loading<HonuHealth>> {
        return HonuHealthApi.get().readSingle(`/api/health`, HonuHealthApi.parse);
    }

}
