MRuby::CrossBuild.new("windows") do |conf|
  conf.toolchain

  conf.gem github: 'hadashiA/mruby-compiler2',
           checksum_hash: '9dc6a4f8782119a4a5d5e294150a8b81721ae4db'
  conf.gem './mrbgems/mrubycs-compiler'

  conf.disable_presym
  conf.cc.defines = %w(MRB_WORD_BOXING MRB_NO_PRESYM MRC_TARGET_MRUBY  MRC_ALLOC_LIBC)
end
