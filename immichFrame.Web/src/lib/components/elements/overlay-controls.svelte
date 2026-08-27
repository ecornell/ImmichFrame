<script lang="ts">
	import {
		mdiChevronRight,
		mdiPlay,
		mdiPause,
		mdiChevronLeft,
		mdiInformationOutline,
		mdiCog
	} from '@mdi/js';
	import { resolve } from '$app/paths';
	import Icon from './icon.svelte';
	import { ProgressBarStatus } from './progress-bar.types';

	interface Props {
		status: ProgressBarStatus;
		overlayVisible: boolean;
		infoVisible: boolean;
		next: () => void;
		back: () => void;
		pause: () => void;
		showInfo: () => void;
	}

	let {
		status = $bindable(),
		overlayVisible,
		infoVisible = $bindable(),
		next,
		back,
		pause,
		showInfo
	}: Props = $props();

	function shortcuts(node: any, shortcutList: any[]) {
		function handleKeyDown(event: { key: any; preventDefault: () => void }) {
			const shortcut = shortcutList.find((s) => s.key === event.key);
			if (shortcut && shortcut.action) {
				event.preventDefault();
				shortcut.action();
			}
		}

		window.addEventListener('keydown', handleKeyDown);

		return {
			destroy() {
				window.removeEventListener('keydown', handleKeyDown);
			}
		};
	}

	// Define your shortcut list
	const shortcutList = $derived([
		{
			key: 'ArrowRight',
			action: next
		},
		{
			key: 'ArrowLeft',
			action: back
		},
		{
			key: ' ',
			action: pause
		},
		{
			key: 'i',
			action: showInfo
		}
	]);
</script>

<svelte:window use:shortcuts={shortcutList} />

{#if overlayVisible}
	<div class="inset-0 z-[100] grid grid-cols-3 gap-2 {infoVisible ? 'hidden' : ''}">
		<div id="overlayback" class="group grid place-items-center">
			<button class="opacity-0 group-hover:opacity-100 text-primary" onclick={back}
				><Icon
					title="Back"
					class="max-h-[min(10rem,33vh)] max-w-[min(10rem,33vh)] h-[33vh] w-[33vw]top"
					path={mdiChevronLeft}
					size=""
				/></button
			>
		</div>

		<div class="grid grid-rows-3 gap-2">
			<div id="overlayInfo" class="group grid place-items-center">
				<button class="opacity-0 hover:opacity-100 text-primary" onclick={showInfo}
					><Icon
						title="Info"
						class="max-h-[min(10rem,33vh)] max-w-[min(10rem,33vh)] h-[33vh] w-[33vw]top"
						path={mdiInformationOutline}
						size=""
					/></button
				>
			</div>

			<div id="overlaypause" class="group grid place-items-center">
				<button onclick={pause} class="opacity-0 group-hover:opacity-100 text-primary">
					<Icon
						class="max-h-[min(10rem,33vh)] max-w-[min(10rem,33vh)] h-[33vh] w-[33vw]"
						title={status == ProgressBarStatus.Paused ? 'Play' : 'Pause'}
						path={status == ProgressBarStatus.Paused ? mdiPlay : mdiPause}
						size=""
					/>
				</button>
			</div>

			<div id="overlaysettings" class="group grid place-items-center">
				<!-- Deliberately smaller than the playback controls: those are primary touch targets on
				a frame, this is an occasional admin action. -->
				<a
					class="text-primary opacity-0 group-hover:opacity-100 focus-visible:opacity-100"
					href={resolve('/settings')}
				>
					<Icon
						title="Settings"
						class="h-[12vh] max-h-[min(4rem,12vh)] w-[12vw] max-w-[min(4rem,12vh)]"
						path={mdiCog}
						size=""
					/>
				</a>
			</div>
		</div>

		<div id="overlaynext" class="group grid place-items-center">
			<button class="opacity-0 group-hover:opacity-100 text-primary" onclick={next}
				><Icon
					title="Next"
					class="max-h-[min(10rem,33vh)] max-w-[min(10rem,33vh)] h-[33vh] w-[33vw]top"
					path={mdiChevronRight}
					size=""
				/>
			</button>
		</div>
	</div>
{/if}
