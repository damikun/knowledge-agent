// Its colors run behind the panel, so the glass has something to blur.
export function SamplePage() {
  return (
    <div aria-hidden="true" className="relative h-full">
      <div className="absolute top-[8%] right-[-6%] size-[34rem] rounded-full bg-[radial-gradient(circle,rgb(99_102_241/0.55),transparent_62%)] blur-2xl" />
      <div className="absolute right-[18%] bottom-[-10%] size-[30rem] rounded-full bg-[radial-gradient(circle,rgb(236_72_153/0.4),transparent_60%)] blur-2xl" />
      <div className="absolute top-[38%] right-[2%] size-[22rem] rounded-full bg-[radial-gradient(circle,rgb(20_184_166/0.45),transparent_60%)] blur-2xl" />
      <div className="relative mx-auto max-w-4xl space-y-7 px-8 py-8">
        <div className="flex items-center justify-between">
          <div className="h-6 w-28 rounded-md bg-current opacity-20" />
          <div className="flex gap-4">
            <div className="h-3 w-14 rounded-full bg-current opacity-15" />
            <div className="h-3 w-14 rounded-full bg-current opacity-15" />
            <div className="h-3 w-14 rounded-full bg-current opacity-15" />
          </div>
        </div>
        <div className="max-w-lg space-y-3 pt-10">
          <div className="h-9 w-4/5 rounded-xl bg-current opacity-20" />
          <div className="h-9 w-3/5 rounded-xl bg-current opacity-20" />
          <div className="h-3.5 w-full rounded-full bg-current opacity-12" />
          <div className="h-3.5 w-5/6 rounded-full bg-current opacity-12" />
          <div className="flex gap-3 pt-3">
            <div className="h-10 w-32 rounded-full bg-current opacity-25" />
            <div className="h-10 w-28 rounded-full bg-current opacity-10" />
          </div>
        </div>
        <div className="grid max-w-2xl grid-cols-3 gap-4 pt-8">
          <div className="h-28 rounded-2xl bg-current opacity-[0.08]" />
          <div className="h-28 rounded-2xl bg-current opacity-[0.08]" />
          <div className="h-28 rounded-2xl bg-current opacity-[0.08]" />
        </div>
      </div>
    </div>
  );
}
