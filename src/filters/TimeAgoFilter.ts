﻿import Vue from "vue";
import * as moment from "moment";

Vue.filter("timeAgo", (date: Date): string => {
	const m = moment(date);
	const now = moment(Date.now()).utc();

	const years = now.diff(m, "years");
	const months = now.diff(m, "months") % 12;

	if (years > 0) {
		return `${years}Y ${months}M`;
	}

	const days = now.diff(m, "days");
	if (months > 0) {
		return `${months}M ${days % 30}d`;
	}

	const hours = now.diff(m, "hours") % 24;
	if (days > 0) {
		return `${days}d ${hours}h`;
	}

	const mins = now.diff(m, "minutes") % 60;
	if (mins > 0) {
        return `${hours}h ${mins}m`;
    }

	const secs = now.diff(m, "seconds") % 60;
	return `0m ${secs}s`;
});
